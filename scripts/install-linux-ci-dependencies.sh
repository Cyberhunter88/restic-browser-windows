#!/usr/bin/env bash
set -euo pipefail

# Kurze Zeitlimits und nur eine Wiederholung je Datei: Ist ein Spiegel nicht erreichbar, muss apt
# innerhalb des Zeitbudgets zum nächsten Eintrag der Spiegelliste wechseln können.
apt_opts=(-o Acquire::Retries=1 -o DPkg::Lock::Timeout=60 -o Acquire::http::Timeout=10 -o Acquire::https::Timeout=10)
azure_mirror='azure.archive.ubuntu.com'
mirror_list='/etc/apt/apt-mirrors.txt'

# Der Azure-Spiegel der GitHub-Runner fällt gelegentlich aus. Nach einem Fehlversuch wird er umgangen:
# aus der Spiegelliste entfernt, sofern dort ein anderer Spiegel bleibt, sonst durch archive.ubuntu.com ersetzt.
bypass_azure_mirror() {
  if [[ -f "$mirror_list" ]] && grep -v "$azure_mirror" "$mirror_list" | grep -q '[^[:space:]]'; then
    echo "Azure-Spiegel wird aus $mirror_list entfernt." >&2
    sudo -n sed -i "/${azure_mirror//./\\.}/d" "$mirror_list"
    return
  fi
  local sources=()
  local file
  for file in "$mirror_list" /etc/apt/sources.list /etc/apt/sources.list.d/*.list /etc/apt/sources.list.d/*.sources; do
    if [[ -f "$file" ]] && grep -q "$azure_mirror" "$file"; then
      sources+=("$file")
    fi
  done
  if [[ "${#sources[@]}" -gt 0 ]]; then
    echo "Azure-Spiegel wird durch archive.ubuntu.com ersetzt: ${sources[*]}" >&2
    sudo -n sed -i "s/${azure_mirror//./\\.}/archive.ubuntu.com/g" "${sources[@]}"
  fi
}

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
  bypass_azure_mirror
  sleep 10
done
