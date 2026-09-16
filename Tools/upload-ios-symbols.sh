#!/usr/bin/env bash
# upload-ios-symbols.sh — ship UnityFramework (and every other dSYM) symbol
# tables to the Bugpunch symbol store as llvm-nm sidecars.
#
# ONE script, two callers:
#   * The Xcode "Bugpunch: upload dSYMs" Run Script phase (installed by the
#     Unity iOS post-process). No args; the dSYM folder comes from Xcode's
#     DWARF_DSYM_FOLDER_PATH and non-Release configurations are skipped
#     unless the Unity build was a Development Build or a CI archive. The
#     archive ALWAYS completes: this mode exits 0 whatever happened.
#   * Standalone, from a shell or CI, to sweep archives the phase missed:
#       BUGPUNCH_SERVER_URL=https://bugpunch.com BUGPUNCH_API_KEY=<project key> \
#         upload-ios-symbols.sh <dsym|xcarchive|dir> [more...]
#     Exits 1 when the sweep did not leave UnityFramework symbols confirmed.
#
# A failed upload is a BUILD WARNING, not a build error. The build still
# archives, uploads and can be tested; each failure prints
#   warning: [BUILD-WARN] [Bugpunch] <what failed>
# `[BUILD-WARN]` is the build-server-neutral marker — this script cannot know
# whether Jenkins, GitHub Actions or a developer's Xcode is running it. A
# pipeline that greps its xcodebuild log for the token flags the build after
# the fact (ODD's Jenkins collects the text after the marker and marks the
# build UNSTABLE), and the `warning:` prefix keeps the line in Xcode's issue
# navigator and in xcpretty-filtered consoles. The script never prints
# `error:` (its own stderr is rewritten below) — that would fail the archive
# here and now. The run ends with ONE `[Bugpunch] OK` / `SKIPPED` /
# `NOT CONFIRMED` / `FAILED` verdict line naming the UnityFramework UUID(s).
#
# Uploads retry with backoff; a sidecar whose upload still fails is parked in
# $BUGPUNCH_SYMBOL_CACHE (default ~/Library/Caches/Bugpunch/pending-symbols)
# and retried at the start of the next run. After uploading, the script asks
# the server again so a UnityFramework UUID that is STILL missing is reported
# instead of assumed.
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
  # Nothing this phase prints may fail the archive. curl and the toolchain write
  # their diagnostics to stderr; a CI wrapper that greps the log for `error:` (or
  # Xcode itself) must never see one from here.
  exec 2> >(sed -l 's/[Ee]rror:/issue:/g' >&2)
fi

discovered=0; uploaded=0; parked=0; failed=0; skipped=0
uf_verified=0; uf_uuids=""; map_status="none"; run_skipped=""

# One line per cause: the pipeline lists the text after the marker on the build.
buildwarn() { echo "warning: [BUILD-WARN] [Bugpunch] $*"; }
# Detail that supports a cause (per-slice outcomes): visible in the log, not listed.
warn() { echo "warning: [Bugpunch] $*" >&2; }
log()  { echo "[Bugpunch] $*"; }
# Non-failure verdicts. CI pipes xcodebuild through xcpretty, which drops every line that
# is not an Xcode diagnostic, so there the OK line goes out as a warning to stay visible.
verdict() {
  if [ "$MODE" = "xcode" ] && [ "${ON_CI:-0}" = "1" ]; then echo "warning: [Bugpunch] $*" >&2; else echo "[Bugpunch] $*"; fi
}

finish() {
  log "symbols: discovered $discovered slice(s), uploaded $uploaded, parked $parked, failed $failed, skipped $skipped"
  local map_note=""
  case "$map_status" in
    uploaded) map_note=", IL2CPP method map uploaded" ;;
    failed)   map_note=", IL2CPP method map FAILED" ;;
  esac
  local status="failed"
  if [ "$uf_verified" = "1" ] && [ $failed -eq 0 ] && [ $parked -eq 0 ] && [ "$map_status" != "failed" ]; then
    status="ok"
    verdict "OK - UnityFramework symbols ${uf_uuids} are on ${SERVER:-the server} (uploaded $uploaded this run$map_note)."
  elif [ -n "$run_skipped" ]; then
    status="skipped"
    verdict "SKIPPED - $run_skipped; crashes on this archive will NOT symbolicate."
  elif [ "$uf_verified" = "1" ]; then
    buildwarn "FAILED - UnityFramework symbols ${uf_uuids} are on ${SERVER:-the server}, but this run left $failed slice(s) failed and $parked parked$map_note. See the [Bugpunch] warning lines above."
  else
    buildwarn "NOT CONFIRMED - UnityFramework symbols for this archive are not verified on ${SERVER:-the server} (uploaded $uploaded, parked $parked, failed $failed, skipped $skipped$map_note); crashes on this build will NOT symbolicate until they are. See the [Bugpunch] warning lines above."
  fi
  [ -n "${TMPDIR_BP:-}" ] && rm -rf "$TMPDIR_BP"
  # The archive completes regardless; only a standalone sweep reports failure by exit code.
  if [ "$MODE" = "xcode" ]; then exit 0; fi
  if [ "$status" = "failed" ]; then exit 1; fi
  exit 0
}
trap finish EXIT

# A CI archive always uploads, whatever its configuration: Jenkins/GitHub tester builds are the
# Debug-configuration Development Builds whose crashes need symbols most.
ON_CI=0
if [ -n "${CI:-}" ] || [ -n "${JENKINS_URL:-}" ] || [ -n "${BUILD_NUMBER:-}" ] || [ -n "${GITHUB_ACTIONS:-}" ]; then ON_CI=1; fi
if [ "$MODE" = "xcode" ] && [ "${CONFIGURATION:-}" != "Release" ] && [ "$DEVBUILD" != "1" ] && [ "$ON_CI" != "1" ]; then
  run_skipped="CONFIGURATION=${CONFIGURATION:-unset} local archive; only Release, Development Build or CI archives upload"
  exit 0
fi

if [ -z "$SERVER" ] || [ -z "$APIKEY" ]; then
  buildwarn "BUGPUNCH_SERVER_URL / BUGPUNCH_API_KEY not set — iOS crashes in this build will NOT symbolicate."
  exit 1
fi

for tool in dwarfdump curl xcrun lipo otool perl gzip; do
  command -v "$tool" >/dev/null 2>&1 || { buildwarn "$tool not found — this script needs the Xcode toolchain (macOS only); symbols were not uploaded."; exit 1; }
done
NM_BIN="$(xcrun --find llvm-nm 2>/dev/null || true)"
if [ -z "$NM_BIN" ]; then buildwarn "llvm-nm not found in the Xcode toolchain — symbols were not uploaded."; exit 1; fi

TMPDIR_BP="$(mktemp -d -t bp-dsyms-XXXXXX)"
mkdir -p "$CACHE_DIR" 2>/dev/null || true

# curl with three attempts and backoff; prints the body on success. The per-attempt
# line is informational (the next attempt may succeed), so curl's own "returned
# error:" text is neutralised there; the caller reports the real outcome.
bp_curl() {
  local attempt delay=2 out
  for attempt in 1 2 3; do
    if out=$(curl -sS --fail --max-time 600 "$@" 2>"$TMPDIR_BP/curl.err"); then
      echo "$out"
      return 0
    fi
    log "request failed (attempt $attempt/3): $(tr -d '\n' <"$TMPDIR_BP/curl.err" | sed 's/[Ee]rror:/issue:/g' | cut -c1-200)"
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
    warn "parked sidecar $p_uuid ($p_filename) from an earlier build still failed to upload — kept in $CACHE_DIR."
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
    buildwarn "DWARF_DSYM_FOLDER_PATH unset or missing — iOS crashes in this build will NOT symbolicate."
    exit 1
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
# crashes need. Locate the just-linked framework binary whenever we can: it is
# the symbol-table source (see step 5 — a dSYM's DWARF companion lists only the
# exported names, the local `t` functions live in the linked binary), and the
# dsymutil source when the project was left on plain 'dwarf' (the debug map is
# still on disk at this phase, so the rebuilt dSYM carries the SAME LC_UUID).
uf_present=0
if [ ${#dsyms[@]} -gt 0 ]; then
  for d in "${dsyms[@]}"; do case "$(basename "$d")" in UnityFramework.dSYM) uf_present=1 ;; esac; done
fi
uf_bin=""
if [ "$MODE" = "xcode" ]; then
  for cand in \
    "${CODESIGNING_FOLDER_PATH:-}/Frameworks/UnityFramework.framework/UnityFramework" \
    "${TARGET_BUILD_DIR:-}/UnityFramework.framework/UnityFramework" \
    "${BUILT_PRODUCTS_DIR:-}/UnityFramework.framework/UnityFramework"; do
    if [ -n "$cand" ] && [ -f "$cand" ]; then uf_bin="$cand"; break; fi
  done
  [ -z "$uf_bin" ] && uf_bin="$(find "${CONFIGURATION_BUILD_DIR:-$DSYM_DIR/..}" -path '*UnityFramework.framework/UnityFramework' -type f -print -quit 2>/dev/null)"
else
  # Standalone sweep of an .xcarchive: the shipped framework sits under Products.
  for arg in "$@"; do
    cand="$(find "$arg" -path '*/Products/Applications/*.app/Frameworks/UnityFramework.framework/UnityFramework' -type f -print -quit 2>/dev/null)"
    if [ -n "$cand" ]; then uf_bin="$cand"; break; fi
  done
fi
[ -n "$uf_bin" ] && log "UnityFramework binary: $uf_bin"
if [ "$MODE" = "xcode" ] && [ $uf_present -eq 0 ] && command -v dsymutil >/dev/null 2>&1; then
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
  buildwarn "no .dSYM bundles found — iOS crashes in this build will NOT symbolicate."
  exit 1
fi

# ── 3. One (uuid, arch, slice, filename) per arch slice ───────────────────
declare -a job_uuid job_arch job_path job_filename
while read -r line; do
  [ -z "$line" ] && continue
  uuid=$(echo "$line" | awk '{print $2}' | tr 'A-F' 'a-f' | tr -d '-')
  arch=$(echo "$line" | awk -F'[()]' '{print $2}')
  src=$(echo "$line" | awk '{for (i=4; i<=NF; i++) printf "%s%s", $i, (i==NF?"":" ")}')
  base=$(basename "$src")
  # The dSYM's DWARF companion identifies the slice (UUID, arch) but its own symbol
  # table lists only the exported names — dsymutil never copies the local `t`
  # functions in. Those live in the linked framework binary, so when the shipped
  # UnityFramework with the SAME UUID is on disk, read the table from it instead.
  if [ "$base" = "UnityFramework" ] && [ -n "$uf_bin" ]; then
    bin_uuid=$(dwarfdump --uuid "$uf_bin" 2>/dev/null | awk -v a="$arch" -F'[() ]+' '$3==a{print $2}' | tr 'A-F' 'a-f' | tr -d '-')
    if [ "$bin_uuid" = "$uuid" ]; then src="$uf_bin"; else warn "linked UnityFramework UUID ($bin_uuid) differs from its dSYM ($uuid) — reading the dSYM."; fi
  fi
  thin="$TMPDIR_BP/${uuid}-${base}"
  if lipo "$src" -thin "$arch" -output "$thin" 2>/dev/null; then src="$thin"; fi
  job_uuid+=("$uuid"); job_arch+=("$arch"); job_path+=("$src"); job_filename+=("${base}.${arch}")
done < <(for d in "${dsyms[@]}"; do dwarfdump --uuid "$d" 2>/dev/null; done)

discovered=${#job_uuid[@]}
if [ $discovered -eq 0 ]; then buildwarn "no UUIDs extracted — were these really dSYMs? iOS crashes in this build will NOT symbolicate."; exit 1; fi
log "discovered $discovered dSYM slice(s)"

has_unityframework=0
for i in "${!job_uuid[@]}"; do
  case "${job_filename[$i]}" in UnityFramework.*) ;; *) continue ;; esac
  has_unityframework=1
  case " $uf_uuids " in *" ${job_uuid[$i]} "*) ;; *) uf_uuids="${uf_uuids:+$uf_uuids }${job_uuid[$i]}" ;; esac
done
[ $has_unityframework -eq 0 ] && buildwarn "UnityFramework.dSYM was not found — iOS crashes in this build will NOT symbolicate. Set the UnityFramework target's 'Debug Information Format' to 'DWARF with dSYM File'."

# ── 4. Ask the server what it lacks ───────────────────────────────────────
items_json="["
for i in "${!job_uuid[@]}"; do
  [ $i -gt 0 ] && items_json+=","
  items_json+="{\"buildId\":\"${job_uuid[$i]}\",\"platform\":\"ios\",\"abi\":\"${job_arch[$i]}\",\"filename\":\"${job_filename[$i]}\"}"
done
items_json+="]"

if ! missing=$(server_missing "$items_json"); then
  buildwarn "$SERVER/api/symbols/check is unreachable — sidecars are parked for the next run, but this archive's symbols are not on the server."
  missing="${job_uuid[*]}"
  check_failed=1
else
  check_failed=0
fi

if [ -z "${missing// }" ]; then
  log "server already has all $discovered symbol slice(s) — nothing to upload."
  [ $has_unityframework -eq 1 ] && uf_verified=1
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
  # would mask the build as symbolicated and block the real table, so refuse it. For
  # UnityFramework that is the build's crash symbols gone — flagged; a vendored framework
  # shipped stripped by its vendor is only log detail.
  set +o pipefail
  gzip -cd "$sidecar" 2>/dev/null | grep -qE ' t [0-9a-f]+ [0-9a-f]+$'
  has_local=$?
  set -o pipefail
  if [ "$has_local" -ne 0 ]; then
    stripped_msg="$filename ($uuid) symbol table is export-only ($sc_size bytes) — the binary is stripped. Keep 'DWARF with dSYM File' and disable 'Strip Linked Product'."
    case "$filename" in
      UnityFramework.*) buildwarn "$stripped_msg iOS crashes in this build will NOT symbolicate." ;;
      *)                warn "$stripped_msg" ;;
    esac
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
      warn "upload failed for $filename ($uuid) — parked in $CACHE_DIR and retried on the next build."
      parked=$((parked + 1))
    else
      warn "upload failed for $filename ($uuid) and it could not be parked — this slice will NOT symbolicate."
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
        buildwarn "UnityFramework symbols are STILL missing on the server for: $still — iOS crashes in this build will NOT symbolicate."
        failed=$((failed + 1))
      else
        log "verified: UnityFramework symbols are on the server."
        uf_verified=1
      fi
    else
      buildwarn "could not re-check $SERVER/api/symbols/check after uploading — UnityFramework symbols for this archive are unverified."
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
    log "IL2CPP method-map uploaded."; map_status="uploaded"
  else
    buildwarn "IL2CPP method-map upload failed — frames will symbolicate to mangled names without C# source lines."; map_status="failed"
  fi
elif [ "$MODE" = "xcode" ]; then
  log "no staged IL2CPP method-map — skipping (no iOS cpp output / no source mapping)."
fi
