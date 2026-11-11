#!/usr/bin/env bash
set -euo pipefail

apt_opts=(-o Acquire::Retries=3 -o DPkg::Lock::Timeout=60 -o Acquire::http::Timeout=30 -o Acquire::https::Timeout=30)

for attempt in 1 2 3; do
  if timeout 240s sudo -n apt-get "${apt_opts[@]}" update \
    && timeout 300s sudo -n env DEBIAN_FRONTEND=noninteractive apt-get "${apt_opts[@]}" install --yes \
      bzip2 gnupg libegl1 libgbm1 libgl1 libgl1-mesa-dri libx11-6 libxcb1 libxkbcommon0 libxkbcommon-x11-0 libwayland-client0 python3 xvfb xauth; then
    exit 0
  fi
  if [[ "$attempt" -eq 3 ]]; then
    echo "Linux-Abhängigkeiten konnten nach drei begrenzten Versuchen nicht installiert werden." >&2
    exit 1
  fi
  sleep 10
done
