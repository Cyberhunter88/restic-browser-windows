#!/usr/bin/env bash
set -euo pipefail

archive="$(realpath -- "${1:?Pfad zum Linux-Archiv fehlt.}")"
root="$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)"
staging="$(mktemp -d)"
trap 'rm -rf "$staging"' EXIT
package_dir="$staging/Portable Restic Browser"
mkdir -p "$package_dir" "$staging/other-directory" "$staging/home"
tar -xzf "$archive" -C "$package_dir"
for entry in ResticBrowser tools/restic; do
  test -x "$package_dir/$entry"
done
test -f "$package_dir/README.md"
test -f "$package_dir/LICENSE"

# Validate the shipped executable, never a restic found through PATH.
"$package_dir/tools/restic" version --json > "$staging/restic-version.json"
python3 - "$root/build/restic-manifest.json" "$staging/restic-version.json" <<'PY'
import json
import sys
with open(sys.argv[1]) as source:
    expected = json.load(source)["version"]
with open(sys.argv[2]) as source:
    actual = json.load(source)
if actual.get("version") != expected or actual.get("go_os") != "linux" or actual.get("go_arch") != "amd64":
    raise SystemExit("Die gebündelte Restic-Version oder Plattform stimmt nicht mit dem Manifest überein.")
PY

# Start from an unrelated working directory and isolate settings/extraction.
cd "$staging/other-directory"
unset RESTIC_BROWSER_ASKPASS_PIPE
set +e
HOME="$staging/home" XDG_DATA_HOME="$staging/home/data" \
  XDG_CONFIG_HOME="$staging/home/config" XDG_CACHE_HOME="$staging/home/cache" \
  DOTNET_BUNDLE_EXTRACT_BASE_DIR="$staging/home/bundle" \
  timeout --signal=TERM --kill-after=5s 15s \
  xvfb-run --auto-servernum --server-args="-screen 0 1280x720x24" \
  "$package_dir/ResticBrowser" > "$staging/startup.log" 2>&1
exit_code=$?
set -e
if [ "$exit_code" -ne 124 ]; then
  cat "$staging/startup.log"
  echo "Die portable Linux-Anwendung wurde beim GUI-Start unerwartet beendet (Exitcode $exit_code)." >&2
  exit 1
fi
echo "Linux-Paket geprüft: Restic-Version, Ausführungsrechte und GUI-Start aus einem Pfad mit Leerzeichen."
