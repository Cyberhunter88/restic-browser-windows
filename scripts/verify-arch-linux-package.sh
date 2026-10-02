#!/usr/bin/env bash
set -euo pipefail

archive="$(realpath -- "${1:?Pfad zum Linux-Archiv fehlt.}")"
root="$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)"

# Test the Ubuntu-built artifact on rolling Arch, without a host SDK/runtime.
# Only the archive, verifier and manifest enter the container, all read-only.
docker run --rm --pull=always \
  --mount "type=bind,source=$archive,target=/input/package.tar.gz,readonly" \
  --mount "type=bind,source=$root/scripts/verify-linux-package.sh,target=/check/scripts/verify-linux-package.sh,readonly" \
  --mount "type=bind,source=$root/build/restic-manifest.json,target=/check/build/restic-manifest.json,readonly" \
  archlinux:base bash -euo pipefail -c '
    pacman -Syu --noconfirm --needed \
      ca-certificates icu krb5 gcc-libs libunwind openssl zlib \
      libx11 libice libsm libxrandr libxi libxcursor fontconfig freetype2 \
      mesa libglvnd libinput ttf-dejavu python xorg-server-xvfb xorg-xauth
    if command -v dotnet || command -v restic; then
      echo "Der Arch-Test darf keine systemweite .NET- oder Restic-Installation verwenden." >&2
      exit 1
    fi
    # The desktop application must work without root privileges.
    runuser -u nobody -- bash /check/scripts/verify-linux-package.sh /input/package.tar.gz
  '
