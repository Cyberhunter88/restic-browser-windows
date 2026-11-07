#!/usr/bin/env sh
# Trägt Restic Browser im Anwendungsmenü ein, damit Taskleiste und Titelleiste (vor allem unter Wayland/KDE)
# das Programmsymbol zeigen. Optional; schreibt nur in den Benutzerbereich ($XDG_DATA_HOME, sonst ~/.local/share).
# Aufruf: sh install-desktop-entry.sh            (installieren, Pfad der entpackten Anwendung wird eingetragen)
#         sh install-desktop-entry.sh --entfernen (wieder entfernen)
set -eu

here="$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)"
data="${XDG_DATA_HOME:-$HOME/.local/share}"
desktop="$data/applications/ResticBrowser.desktop"
icon="$data/icons/hicolor/512x512/apps/ResticBrowser.png"

if [ "${1:-}" = "--entfernen" ]; then
  rm -f "$desktop" "$icon"
  echo "Eintrag entfernt."
  exit 0
fi

[ -x "$here/ResticBrowser" ] || { echo "Fehler: ResticBrowser liegt nicht neben diesem Skript." >&2; exit 1; }
[ -f "$here/share/ResticBrowser.png" ] && [ -f "$here/share/ResticBrowser.desktop" ] ||
  { echo "Fehler: Dateien im Ordner share fehlen." >&2; exit 1; }

# In Desktop-Einträgen sind Anführungszeichen, Backtick, Dollar, Backslash und Prozent im Exec-Pfad heikel.
case "$here" in
  *[\"\`\$\\%]*) echo "Fehler: Der Ordnerpfad enthält Sonderzeichen (\" \` \$ \\ %). Bitte in einen einfacheren Pfad verschieben." >&2; exit 1 ;;
esac
mkdir -p "$data/applications" "$data/icons/hicolor/512x512/apps"
cp "$here/share/ResticBrowser.png" "$icon"
while IFS= read -r line; do
  case "$line" in
    Exec=@EXEC@) printf 'Exec="%s"\n' "$here/ResticBrowser" ;;
    *) printf '%s\n' "$line" ;;
  esac
done < "$here/share/ResticBrowser.desktop" > "$desktop"
chmod 644 "$desktop"
command -v update-desktop-database >/dev/null 2>&1 && update-desktop-database "$data/applications" >/dev/null 2>&1 || true
command -v gtk-update-icon-cache >/dev/null 2>&1 && gtk-update-icon-cache -q -t "$data/icons/hicolor" >/dev/null 2>&1 || true
echo "Eintrag angelegt: $desktop"
echo "Das Symbol erscheint nach dem nächsten Start (gegebenenfalls einmal ab- und wieder anmelden)."
