#!/usr/bin/env bash
# upload-ios-symbols.sh — ship UnityFramework (and every other dSYM) symbol
# tables to the Bugpunch symbol store as llvm-nm sidecars.
#
# ONE script, two callers:
#   * The Xcode "Bugpunch: upload dSYMs" Run Script phase (installed by the
#     Unity iOS post-process). No args; the dSYM folder comes from Xcode's
#     DWARF_DSYM_FOLDER_PATH and non-Release configurations are skipped
#     unless the Unity build was a Development Build. NEVER fails the build.
#   * Standalone, from a shell or CI, to sweep archives the phase missed:
#       BUGPUNCH_SERVER_URL=https://bugpunch.com BUGPUNCH_API_KEY=<project key> \
#         upload-ios-symbols.sh <dsym|xcarchive|dir> [more...]
#     Exits non-zero when anything could not be uploaded.
#
# Both modes: every skip prints a visible `warning:` line, uploads retry with
# backoff, a sidecar whose upload still fails is parked in
# $BUGPUNCH_SYMBOL_CACHE (default ~/Library/Caches/Bugpunch/pending-symbols)
# and retried at the start of the next run, and after uploading the script
# asks the server again so a UnityFramework UUID that is STILL missing is
# reported instead of assumed.
#
# We upload the symbol TABLE the server needs, not the whole dSYM: a Unity
# UnityFramework dSYM is ~1 GB, symbolication only consumes the
# `llvm-nm -n -P --defined-only --print-size` output (~15 MB gzipped).

set -uo pipefail

MODE="xcode"
if [ $# -gt 0 ]; then MODE="standalone"; fi

SERVER="${BUGPUNCH_SERVER_URL:-}"
SERVER="${SERVER%/}"
APIKEY="${BUGPUNCH_API_KEY:-}"
DEVBUILD="${BUGPUNCH_DEVBUILD:-0}"
MAP_FILE="${BUGPUNCH_MAP_FILE:-}"
CACHE_DIR="${BUGPUNCH_SYMBOL_CACHE:-$HOME/Library/Caches/Bugpunch/pending-symbols}"

if [ "$MODE" = "xcode" ]; then
  # Jenkins and other CI wrappers `grep -q error:` the build log to decide
  # whether an Archive succeeded; curl's own diagnostics must not trip them.
  exec 2> >(sed 's/[Ee]rror:/issue:/g' >&2)
fi

discovered=0; uploaded=0; parked=0; failed=0; skipped=0

warn() { echo "warning: [Bugpunch] $*" >&2; }
log()  { echo "[Bugpunch] $*"; }

finish() {
  log "symbols: discovered $discovered slice(s), uploaded $uploaded, parked $parked, failed $failed, skipped $skipped"
  [ -n "${TMPDIR_BP:-}" ] && rm -rf "$TMPDIR_BP"
  if [ "$MODE" = "xcode" ]; then exit 0; fi
  if [ $failed -gt 0 ] || [ $parked -gt 0 ]; then exit 1; fi
  exit 0
}
trap finish EXIT

if [ "$MODE" = "xcode" ] && [ "${CONFIGURATION:-}" != "Release" ] && [ "$DEVBUILD" != "1" ]; then
  log "skipping dSYM upload — CONFIGURATION=${CONFIGURATION:-unset}, only Release (or Development Build) uploads."
  exit 0
fi

if [ -z "$SERVER" ] || [ -z "$APIKEY" ]; then
  warn "BUGPUNCH_SERVER_URL / BUGPUNCH_API_KEY not set — iOS crashes in this build will NOT symbolicate."
  exit 0
fi

for tool in dwarfdump curl xcrun lipo otool perl gzip; do
  command -v "$tool" >/dev/null 2>&1 || { warn "$tool not found — this script needs the Xcode toolchain (macOS only)."; exit 0; }
done
NM_BIN="$(xcrun --find llvm-nm 2>/dev/null || true)"
if [ -z "$NM_BIN" ]; then warn "llvm-nm not found in the Xcode toolchain — skipping symbol upload."; exit 0; fi

TMPDIR_BP="$(mktemp -d -t bp-dsyms-XXXXXX)"
mkdir -p "$CACHE_DIR" 2>/dev/null || true

# curl with three attempts and backoff; prints the body on success.
bp_curl() {
  local attempt delay=2 out
  for attempt in 1 2 3; do
    if out=$(curl -sS --fail --max-time 600 "$@" 2>"$TMPDIR_BP/curl.err"); then
      echo "$out"
      return 0
    fi
    log "request failed (attempt $attempt/3): $(tr -d '\n' <"$TMPDIR_BP/curl.err" | cut -c1-200)"
    [ $attempt -lt 3 ] && sleep $delay
    delay=$((delay * 3))
  done
  return 1
}

# {"missing":["uuid",…]} → space-separated uuids; prints nothing on error.
server_missing() {
  local resp
  resp=$(bp_curl -X POST -H "X-Api-Key: $APIKEY" -H "Content-Type: application/json" --data "{\"items\":$1}" "$SERVER/api/symbols/check") || return 1
  echo "$resp" | sed -E 's/.*"missing":\[([^]]*)\].*/\1/' | tr -d '"' | tr ',' ' '
}

upload_sidecar() {  # uuid abi filename path
  bp_curl -X POST -H "X-Api-Key: $APIKEY" \
    -F "buildId=$1" -F "platform=ios" -F "abi=$2" -F "filename=$3" \
    -F "file=@$4;type=application/gzip;filename=$1.symtab.gz" \
    "$SERVER/api/symbols/sidecar/upload" >/dev/null
}

# ── 1. Retry sidecars parked by an earlier run ─────────────────────────────
for pending in "$CACHE_DIR"/*.symtab.gz; do
  [ -f "$pending" ] || continue
  base=$(basename "$pending" .symtab.gz)
  p_uuid=${base%%__*}; rest=${base#*__}; p_abi=${rest%%__*}; p_filename=${rest#*__}
  missing=$(server_missing "[{\"buildId\":\"$p_uuid\",\"platform\":\"ios\",\"abi\":\"$p_abi\",\"filename\":\"$p_filename\"}]") || { warn "server check failed while retrying parked $p_uuid — keeping it for next time."; continue; }
  if [[ " $missing " != *" $p_uuid "* ]]; then
    log "parked $p_uuid is already on the server — dropping it."
    rm -f "$pending"; continue
  fi
  if upload_sidecar "$p_uuid" "$p_abi" "$p_filename" "$pending"; then
    log "uploaded parked sidecar $p_uuid ($p_filename)"
    uploaded=$((uploaded + 1)); rm -f "$pending"
  else
    warn "parked sidecar $p_uuid still failed to upload — keeping it in $CACHE_DIR."
    parked=$((parked + 1))
  fi
done

# ── 2. Discover dSYMs ─────────────────────────────────────────────────────
dsyms=()
add_dsyms_under() {
  while IFS= read -r line; do dsyms+=("$line"); done < <(find "$1" -name '*.dSYM' -type d -print 2>/dev/null)
  return 0
}
if [ "$MODE" = "xcode" ]; then
  DSYM_DIR="${DWARF_DSYM_FOLDER_PATH:-}"
  if [ -z "$DSYM_DIR" ] || [ ! -d "$DSYM_DIR" ]; then
    warn "DWARF_DSYM_FOLDER_PATH unset or missing — iOS crashes in this build will NOT symbolicate."
    exit 0
  fi
  add_dsyms_under "$DSYM_DIR"
else
  for arg in "$@"; do
    if [ ! -d "$arg" ]; then warn "skipping non-directory: $arg"; continue; fi
    case "$arg" in
      *.dSYM) dsyms+=("$arg") ;;
      *)      add_dsyms_under "$arg" ;;
    esac
  done
fi

# UnityFramework carries the engine + IL2CPP game code; it is the ONE dSYM
# crashes need. In Xcode mode, if the project setting was left on plain
# 'dwarf', rebuild it from the just-linked binary (the debug map is still on
# disk at this phase) so it carries the SAME LC_UUID as the shipped binary.
uf_present=0
if [ ${#dsyms[@]} -gt 0 ]; then
  for d in "${dsyms[@]}"; do case "$(basename "$d")" in UnityFramework.dSYM) uf_present=1 ;; esac; done
fi
if [ "$MODE" = "xcode" ] && [ $uf_present -eq 0 ] && command -v dsymutil >/dev/null 2>&1; then
  uf_bin=""
  for cand in \
    "${CODESIGNING_FOLDER_PATH:-}/Frameworks/UnityFramework.framework/UnityFramework" \
    "${TARGET_BUILD_DIR:-}/UnityFramework.framework/UnityFramework" \
    "${BUILT_PRODUCTS_DIR:-}/UnityFramework.framework/UnityFramework"; do
    if [ -n "$cand" ] && [ -f "$cand" ]; then uf_bin="$cand"; break; fi
  done
  [ -z "$uf_bin" ] && uf_bin="$(find "${CONFIGURATION_BUILD_DIR:-$DSYM_DIR/..}" -path '*UnityFramework.framework/UnityFramework' -type f -print -quit 2>/dev/null)"
  if [ -n "$uf_bin" ] && [ -f "$uf_bin" ]; then
    log "UnityFramework.dSYM absent — synthesizing from $uf_bin via dsymutil."
    if dsymutil "$uf_bin" -o "$TMPDIR_BP/UnityFramework.dSYM" 2>/dev/null; then
      dsyms+=("$TMPDIR_BP/UnityFramework.dSYM"); uf_present=1
    else
      log "dsymutil could not rebuild UnityFramework.dSYM (binary likely stripped)."
    fi
  fi
fi

if [ ${#dsyms[@]} -eq 0 ]; then
  warn "no .dSYM bundles found — iOS crashes in this build will NOT symbolicate."
  exit 0
fi

# ── 3. One (uuid, arch, slice, filename) per arch slice ───────────────────
declare -a job_uuid job_arch job_path job_filename
while read -r line; do
  [ -z "$line" ] && continue
  uuid=$(echo "$line" | awk '{print $2}' | tr 'A-F' 'a-f' | tr -d '-')
  arch=$(echo "$line" | awk -F'[()]' '{print $2}')
  src=$(echo "$line" | awk '{for (i=4; i<=NF; i++) printf "%s%s", $i, (i==NF?"":" ")}')
  base=$(basename "$src")
  thin="$TMPDIR_BP/${uuid}-${base}"
  if lipo "$src" -thin "$arch" -output "$thin" 2>/dev/null; then src="$thin"; fi
  job_uuid+=("$uuid"); job_arch+=("$arch"); job_path+=("$src"); job_filename+=("${base}.${arch}")
done < <(for d in "${dsyms[@]}"; do dwarfdump --uuid "$d" 2>/dev/null; done)

discovered=${#job_uuid[@]}
if [ $discovered -eq 0 ]; then warn "no UUIDs extracted — were these really dSYMs?"; exit 0; fi
log "discovered $discovered dSYM slice(s)"

has_unityframework=0
for fn in "${job_filename[@]}"; do case "$fn" in UnityFramework.*) has_unityframework=1 ;; esac; done
[ $has_unityframework -eq 0 ] && warn "UnityFramework.dSYM was not found — iOS crashes in this build will NOT symbolicate. Set the UnityFramework target's 'Debug Information Format' to 'DWARF with dSYM File'."

# ── 4. Ask the server what it lacks ───────────────────────────────────────
items_json="["
for i in "${!job_uuid[@]}"; do
  [ $i -gt 0 ] && items_json+=","
  items_json+="{\"buildId\":\"${job_uuid[$i]}\",\"platform\":\"ios\",\"abi\":\"${job_arch[$i]}\",\"filename\":\"${job_filename[$i]}\"}"
done
items_json+="]"

if ! missing=$(server_missing "$items_json"); then
  warn "$SERVER/api/symbols/check is unreachable — building sidecars anyway and parking them for the next run."
  missing="${job_uuid[*]}"
  check_failed=1
else
  check_failed=0
fi

if [ -z "${missing// }" ]; then
  log "server already has all $discovered symbol slice(s) — nothing to upload."
  exit 0
fi

# ── 5. Build + upload each missing sidecar ────────────────────────────────
for i in "${!job_uuid[@]}"; do
  uuid="${job_uuid[$i]}"; src="${job_path[$i]}"; filename="${job_filename[$i]}"; abi="${job_arch[$i]}"
  if [[ " $missing " != *" $uuid "* ]]; then continue; fi

  sidecar="$TMPDIR_BP/${uuid}.symtab.gz"
  # Addresses become image-offset relative (pc - image base): __TEXT vmaddr is 0
  # for dylibs and 0x100000000 for the main executable after __PAGEZERO.
  textva="$(otool -arch "$abi" -l "$src" 2>/dev/null | awk '/segname __TEXT/{t=1} t&&/vmaddr/{print $2; exit}')"
  [ -n "$textva" ] || textva=0
  if ! "$NM_BIN" -n -P --defined-only --print-size "$src" 2>/dev/null \
       | perl -e '
           my $base = hex($ARGV[0]);
           while (<STDIN>) {
             chomp; s/^\s+//;
             my @f = split(/\s+/, $_, 4);
             if (@f >= 3 && $f[2] =~ /^[0-9a-fA-F]+$/) {
               my $off = hex($f[2]) - $base;
               next if $off < 0;
               $f[2] = sprintf("%x", $off);
             }
             print join(" ", @f), "\n";
           }' "$textva" \
       | gzip -c > "$sidecar"; then
    warn "llvm-nm failed for $filename ($uuid) — this slice will NOT symbolicate."
    rm -f "$sidecar"; failed=$((failed + 1)); continue
  fi
  sc_size=$(wc -c < "$sidecar" | tr -d ' ')

  # A stripped binary yields an export-only table (no local `t` symbols). Uploading it
  # would mask the build as symbolicated and block the real table, so refuse it.
  set +o pipefail
  gzip -cd "$sidecar" 2>/dev/null | grep -qE ' t [0-9a-f]+ [0-9a-f]+$'
  has_local=$?
  set -o pipefail
  if [ "$has_local" -ne 0 ]; then
    warn "$filename ($uuid) symbol table is export-only ($sc_size bytes) — the binary is stripped, iOS crashes will NOT symbolicate. Keep 'DWARF with dSYM File' and disable 'Strip Linked Product' for UnityFramework."
    rm -f "$sidecar"; skipped=$((skipped + 1)); continue
  fi

  if [ "$check_failed" -eq 1 ]; then
    cp "$sidecar" "$CACHE_DIR/${uuid}__${abi}__${filename}.symtab.gz" && parked=$((parked + 1))
    log "parked $filename ($uuid, $sc_size bytes) in $CACHE_DIR"
    continue
  fi

  log "uploading symtab sidecar $filename ($uuid, $abi, $sc_size bytes)"
  if upload_sidecar "$uuid" "$abi" "$filename" "$sidecar"; then
    uploaded=$((uploaded + 1))
  else
    if cp "$sidecar" "$CACHE_DIR/${uuid}__${abi}__${filename}.symtab.gz" 2>/dev/null; then
      warn "upload failed for $filename ($uuid) — parked in $CACHE_DIR, retried on the next build."
      parked=$((parked + 1))
    else
      warn "upload failed for $filename ($uuid) and it could not be parked — iOS crashes will NOT symbolicate."
      failed=$((failed + 1))
    fi
  fi
  rm -f "$sidecar"
done

# ── 6. Verify: the UnityFramework UUID(s) must now be on the server ───────
if [ "$check_failed" -eq 0 ]; then
  verify_json="["; sep=""
  for i in "${!job_uuid[@]}"; do
    case "${job_filename[$i]}" in UnityFramework.*) ;; *) continue ;; esac
    verify_json+="${sep}{\"buildId\":\"${job_uuid[$i]}\",\"platform\":\"ios\",\"abi\":\"${job_arch[$i]}\",\"filename\":\"${job_filename[$i]}\"}"
    sep=","
  done
  verify_json+="]"
  if [ "$verify_json" != "[]" ]; then
    if still=$(server_missing "$verify_json"); then
      if [ -n "${still// }" ]; then
        warn "UnityFramework symbols are STILL missing on the server for: $still — iOS crashes in this build will NOT symbolicate."
        failed=$((failed + 1))
      else
        log "verified: UnityFramework symbols are on the server."
      fi
    fi
  fi
fi

# ── 7. IL2CPP method map (C# method → source file:line) ───────────────────
if [ -n "$MAP_FILE" ] && [ -f "$MAP_FILE" ]; then
  ids_json="["; sep=""; seen=" "
  for i in "${!job_uuid[@]}"; do
    case "${job_filename[$i]}" in UnityFramework.*) ;; *) continue ;; esac
    u="${job_uuid[$i]}"
    case "$seen" in *" $u "*) continue ;; esac
    seen="$seen$u "; ids_json+="${sep}\"$u\""; sep=","
  done
  if [ "$ids_json" = "[" ]; then
    seen=" "; sep=""
    for u in "${job_uuid[@]}"; do
      case "$seen" in *" $u "*) continue ;; esac
      seen="$seen$u "; ids_json+="${sep}\"$u\""; sep=","
    done
  fi
  ids_json+="]"
  map_bytes=$(wc -c < "$MAP_FILE" | tr -d ' ')
  log "uploading IL2CPP method-map ($map_bytes bytes)"
  if bp_curl -X POST -H "X-Api-Key: $APIKEY" -F "buildIds=$ids_json" \
      -F "file=@$MAP_FILE;type=application/gzip;filename=il2cpp_mapping.json.gz" \
      "$SERVER/api/symbols/il2cpp-mapping/upload-multi" >/dev/null; then
    log "IL2CPP method-map uploaded."
  else
    warn "IL2CPP method-map upload failed — frames will symbolicate to mangled names without C# source lines."
  fi
elif [ "$MODE" = "xcode" ]; then
  log "no staged IL2CPP method-map — skipping (no iOS cpp output / no source mapping)."
fi
