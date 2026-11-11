#!/usr/bin/env sh
# Baut die Linux-Version der Rust-Anwendung mit mitgeliefertem, geprüftem restic (tools/restic).
# Benötigt: cargo, pwsh + gpg + bzip2 (für scripts/prepare-restic.ps1), python3.
set -eu

root="$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)"
dist="$root/dist-rust"
archive="$dist/ResticBrowser-linux-x64.tar.gz"
staging="$(mktemp -d)"
trap 'rm -rf "$staging"' EXIT

mkdir -p "$dist" "$staging/package/tools" "$staging/package/share"
(cd "$root" && cargo build --profile dist --locked -p restic-ui \
  && python3 scripts/third-party.py "$staging/package/THIRD-PARTY.md" --target x86_64-unknown-linux-gnu)
cp "$root/target/dist/restic-browser" "$staging/package/ResticBrowser"
cp "$root/LICENSE" "$root/README.md" "$staging/package/"
cp "$root/packaging/linux/ResticBrowser.png" "$root/packaging/linux/ResticBrowser.desktop" "$staging/package/share/"
cp "$root/packaging/linux/install-desktop-entry.sh" "$staging/package/"
pwsh -NoProfile -File "$root/scripts/prepare-restic.ps1" -Platform linux -Destination "$staging/package/tools/restic"
chmod 755 "$staging/package/ResticBrowser" "$staging/package/tools/restic" "$staging/package/install-desktop-entry.sh"
test -x "$staging/package/tools/restic"

rm -f "$archive"
tar -C "$staging/package" -czf "$archive" ResticBrowser LICENSE README.md THIRD-PARTY.md install-desktop-entry.sh share tools/restic
sha256sum "$archive" | tee "$dist/SHA256SUMS.txt"
