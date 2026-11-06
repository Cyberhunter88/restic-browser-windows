#!/usr/bin/env bash
# Erzeugt bereinigte restic-JSON-Fixtures aus einem Wegwerf-Repository.
# Benötigt: restic (Version aus build/restic-manifest.json), bash, sed.
# Es werden keine echten Pfade, Benutzernamen oder Passwörter gespeichert.
set -euo pipefail
export MSYS_NO_PATHCONV=1
cd "$(dirname "$0")"

VER="$(restic version | awk '{print $2}')"
OUT="$PWD/restic-$VER"
rm -f "$OUT"/* 2>/dev/null || true
mkdir -p "$OUT"

W="$(mktemp -d)"; W="$(cygpath -m "$W" 2>/dev/null || echo "$W")"
cleanup() { chmod -R u+w "$W" 2>/dev/null || true; rm -rf "$W" 2>/dev/null || true; }
trap cleanup EXIT

export RESTIC_PASSWORD="fixture-only-throwaway"
R="$W/repo"
S="$W/src"
mkdir -p "$S/docs/sub" "$S/Übung"
printf 'hallo welt\n' > "$S/docs/a.txt"
printf 'zwei\n' > "$S/docs/sub/b.txt"
printf 'ä\n' > "$S/Übung/über.txt"

restic init -r "$R" --quiet > /dev/null
restic backup -r "$R" "$S" --host fixture-host --tag fixture --json > "$OUT/backup-1.jsonl"
printf 'neu\n' >> "$S/docs/a.txt"
restic backup -r "$R" "$S" --host fixture-host --tag fixture --json > "$OUT/backup-2.jsonl"

restic version --json > "$OUT/version.json"
restic snapshots -r "$R" --json --no-lock > "$OUT/snapshots.json"
restic ls -r "$R" latest --json --no-lock > "$OUT/ls-snapshot.jsonl"
restic ls -r "$R" latest --json --no-lock --recursive / > "$OUT/ls-recursive.jsonl"
restic find -r "$R" --json --no-lock "a.txt" > "$OUT/find-a.json"
restic find -r "$R" --json --no-lock "*.nichtda" > "$OUT/find-empty.json"
restic stats -r "$R" --json --no-lock > "$OUT/stats.json"
restic check -r "$R" --no-lock > "$OUT/check.txt" 2>&1
RESTIC_PASSWORD=falsch restic snapshots -r "$R" --json --no-lock > /dev/null 2> "$OUT/error-wrongpassword.txt" || true
restic snapshots -r "$W/nichtda" --json --no-lock > /dev/null 2> "$OUT/error-norepo.txt" || true

# Bereinigung: Temp-Pfade, Benutzer- und Rechnernamen ersetzen.
WM="$(cygpath -m "$W" 2>/dev/null || echo "$W")"   # C:/Users/.../tmp.X
WB="${WM//\//\\\\}"                                 # C:\\Users\\... (so steht es in JSON)
WS="${WM//\//\\}"                                   # C:\Users\... (Klartext-Ausgaben)
WP="/${WM/:/}"                                      # /C/Users/... (restic-Pfad)
esc() { printf '%s' "$1" | sed 's/[][\.*^$#]/\\&/g'; }
for f in "$OUT"/*; do
  sed -i \
    -e "s#$(esc "$WB")#/fixture#g" -e "s#$(esc "$WS")#/fixture#g" \
    -e "s#$(esc "$WP")#/fixture#g" -e "s#$(esc "$WM")#/fixture#g" \
    -e 's#"username":"[^"]*"#"username":"fixture-user"#g' \
    "$f"
done

# Windows-Trennzeichen nach dem Platzhalter vereinheitlichen.
sed -i -e 's#/fixture\\\\#/fixture/#g' -e 's#/fixture\\#/fixture/#g' "$OUT"/*

# Übergeordnete Ordner des Temp-Pfads (z. B. /C/Users/<name>/...) aus ls-Ausgaben entfernen,
# damit kein echter Benutzername im Verzeichnisbaum verbleibt.
PREFIX=""
IFS='/' read -ra PARTS <<< "${WP#/}"
for part in "${PARTS[@]}"; do
  PREFIX="$PREFIX/$part"
  [ "$PREFIX" = "$WP" ] && break
  sed -i "\#\"path\":\"$(esc "$PREFIX")\"#d" "$OUT"/ls-*.jsonl
done

# Klartext-Ausgabe von restic check: lokalen Cache-Pfad neutralisieren.
sed -i 's#C:\\Users\\[^ ]*#/cache#g' "$OUT/check.txt"

echo "Fixtures in $OUT"
