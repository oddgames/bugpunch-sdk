#!/usr/bin/env bash
# upload-ios-symbols.sh — extract UUIDs from one or more dSYM bundles and
# push their DWARF binaries to the Bugpunch symbol store.
#
# macOS-only (needs `dwarfdump`). Intended to run from your CI after
# `xcodebuild archive` completes — that's when the dSYMs actually exist.
#
# Usage:
#   BUGPUNCH_SERVER_URL=https://bugpunchserver-xxxx.b4a.run \
#   BUGPUNCH_API_KEY=<your-key> \
#   upload-ios-symbols.sh <dsym-path-or-dir> [more paths...]
#
# Paths can be:
#   * A direct .dSYM bundle (My.app.dSYM)
#   * A directory — every *.dSYM inside is uploaded recursively
#   * An Xcode archive — we dive into dSYMs/ automatically
#
# Matches the Bugpunch server's /api/symbols flow: we first ask the server
# which UUIDs it already has and only upload the missing ones. Multi-arch
# dSYMs (rare in 2024+ since iOS 11 dropped 32-bit) get one upload per arch
# slice — we `lipo -thin` them before uploading.
set -euo pipefail

: "${BUGPUNCH_SERVER_URL:?set BUGPUNCH_SERVER_URL}"
: "${BUGPUNCH_API_KEY:?set BUGPUNCH_API_KEY (same key your game SDK uses)}"

if [[ $# -lt 1 ]]; then
  echo "usage: $0 <dsym-or-dir> [more...]" >&2
  exit 64
fi

SERVER="${BUGPUNCH_SERVER_URL%/}"
TMPDIR="$(mktemp -d -t bp-dsyms-XXXXXX)"
trap 'rm -rf "$TMPDIR"' EXIT

command -v dwarfdump >/dev/null 2>&1 || {
  echo "dwarfdump not found — this script is macOS-only" >&2; exit 69;
}
command -v curl >/dev/null 2>&1 || { echo "curl required" >&2; exit 69; }

# ── Discover every .dSYM bundle under the given paths ──

dsyms=()
for arg in "$@"; do
  if [[ -d "$arg" && "$arg" == *.dSYM ]]; then
    dsyms+=("$arg")
  elif [[ -d "$arg" ]]; then
    while IFS= read -r line; do dsyms+=("$line"); done < \
      <(find "$arg" -name "*.dSYM" -type d -print)
  else
    echo "skipping non-directory: $arg" >&2
  fi
done

if [[ ${#dsyms[@]} -eq 0 ]]; then
  echo "no .dSYM bundles found in: $*" >&2
  exit 65
fi

# ── For each dSYM: one (UUID, arch, binary-slice) per arch slice. ──
# UUIDs come from `dwarfdump --uuid` which prints, e.g.:
#   UUID: 1A2B... (arm64) /path/to/Contents/Resources/DWARF/MyApp

declare -a job_uuid job_arch job_path job_filename
while read -r line; do
  [[ -z "$line" ]] && continue
  uuid=$(echo "$line" | awk '{print $2}' | tr 'A-F' 'a-f' | tr -d '-')
  arch=$(echo "$line" | awk -F'[()]' '{print $2}')
  src=$(echo "$line" | awk '{for (i=4; i<=NF; i++) printf "%s%s", $i, (i==NF?"":" ")}')
  base=$(basename "$src")

  # Thin multi-arch slices so each upload matches exactly one UUID.
  if [[ $(echo "$line" | wc -l) -gt 0 ]] && command -v lipo >/dev/null 2>&1; then
    thin="$TMPDIR/${uuid}-${base}"
    if lipo "$src" -thin "$arch" -output "$thin" 2>/dev/null; then
      src="$thin"
    fi
  fi

  job_uuid+=("$uuid")
  job_arch+=("$arch")
  job_path+=("$src")
  job_filename+=("${base}.${arch}")
done < <(for d in "${dsyms[@]}"; do dwarfdump --uuid "$d" 2>/dev/null; done)

if [[ ${#job_uuid[@]} -eq 0 ]]; then
  echo "no UUIDs extracted — were these really dSYMs?" >&2; exit 65
fi

echo "[bugpunch] discovered ${#job_uuid[@]} dSYM slice(s)"

# ── Ask server which UUIDs it's missing ──

items_json="["
for i in "${!job_uuid[@]}"; do
  [[ $i -gt 0 ]] && items_json+=","
  items_json+="{\"buildId\":\"${job_uuid[$i]}\",\"platform\":\"ios\",\"abi\":\"${job_arch[$i]}\",\"filename\":\"${job_filename[$i]}\"}"
done
items_json+="]"

check_resp=$(curl -fsS -X POST \
  -H "X-Api-Key: $BUGPUNCH_API_KEY" \
  -H "Content-Type: application/json" \
  --data "{\"items\":$items_json}" \
  "$SERVER/api/symbols/check")

# Tiny grep-based parser: the response is `{"missing":["uuid1","uuid2",…]}`.
missing_raw=$(echo "$check_resp" | sed -E 's/.*"missing":\[([^]]*)\].*/\1/')
missing=$(echo "$missing_raw" | tr -d '"' | tr ',' ' ')

if [[ -z "$missing" || "$missing" == "$missing_raw" && "$missing_raw" == "" ]]; then
  echo "[bugpunch] server already has all symbols — nothing to upload."
  exit 0
fi

# ── Upload only the missing slices ──

uploaded=0
for i in "${!job_uuid[@]}"; do
  uuid="${job_uuid[$i]}"
  if ! [[ " $missing " == *" $uuid "* ]]; then continue; fi

  echo "[bugpunch] uploading ${uuid} (${job_arch[$i]}, $(du -h "${job_path[$i]}" | awk '{print $1}'))"
  curl -fsS -X POST \
    -H "X-Api-Key: $BUGPUNCH_API_KEY" \
    -F "buildId=$uuid" \
    -F "platform=ios" \
    -F "abi=${job_arch[$i]}" \
    -F "filename=${job_filename[$i]}" \
    -F "file=@${job_path[$i]}" \
    "$SERVER/api/symbols/upload" >/dev/null
  uploaded=$((uploaded + 1))
done

echo "[bugpunch] uploaded $uploaded slice(s)"
