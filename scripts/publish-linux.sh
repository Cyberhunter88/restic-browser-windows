#!/usr/bin/env sh
set -eu

configuration="${1:-Release}"
root="$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)"
archive="$root/dist/ResticBrowser-linux-x64.tar.gz"
staging="$(mktemp -d)"
trap 'rm -rf "$staging"' EXIT

dotnet publish "$root/src/ResticBrowser/ResticBrowser.csproj" -c "$configuration" -r linux-x64 --self-contained true -o "$staging"
test -x "$staging/ResticBrowser"

package_dir="$staging/package"
mkdir -p "$package_dir"
cp "$staging/ResticBrowser" "$package_dir/ResticBrowser"
cp "$root/LICENSE" "$root/README.md" "$package_dir/"
chmod 755 "$package_dir/ResticBrowser"

mkdir -p "$root/dist"
rm -f "$archive"
tar -C "$package_dir" -czf "$archive" ResticBrowser LICENSE README.md
sha256sum "$archive"
