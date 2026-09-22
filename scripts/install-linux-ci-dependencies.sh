#!/usr/bin/env bash
set -euo pipefail

for attempt in 1 2; do
  if timeout 120s sudo -n apt-get -o Acquire::Retries=3 -o DPkg::Lock::Timeout=60 -o Acquire::http::Timeout=30 -o Acquire::https::Timeout=30 update \
    && timeout 120s sudo -n env DEBIAN_FRONTEND=noninteractive apt-get -o Acquire::Retries=3 -o DPkg::Lock::Timeout=60 -o Acquire::http::Timeout=30 -o Acquire::https::Timeout=30 install --yes \
      bzip2 gnupg libegl1 libgbm1 libgl1 libgl1-mesa-dri libinput10 openssh-server xvfb; then
    exit 0
  fi
  if [[ "$attempt" -eq 2 ]]; then
    echo "Linux-Abhängigkeiten konnten nach zwei begrenzten Versuchen nicht installiert werden." >&2
    exit 1
  fi
  sleep 3
done
