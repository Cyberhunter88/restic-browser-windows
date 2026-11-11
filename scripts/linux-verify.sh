#!/usr/bin/env sh
# Prüft die Rust-Version unter Linux (CachyOS/Arch, Debian/Ubuntu): Werkzeuge, fmt, clippy, Tests mit restic,
# Lizenzen, Schwachstellen, Release-Build, Start unter Xvfb mit Screenshot und optional das Paket.
# Aufruf aus dem Repository: sh scripts/linux-verify.sh [--package]
# Es werden nur temporäre Verzeichnisse (mktemp) beschrieben; Einstellungen des Benutzers bleiben unberührt.
set -u

root="$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)"
cd "$root"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT
failed=0
step() { printf '\n== %s\n' "$1"; }
run() { desc="$1"; shift; step "$desc"; if "$@"; then echo "OK: $desc"; else echo "FEHLER: $desc"; failed=1; fi; }
need() { command -v "$1" >/dev/null 2>&1 || { echo "FEHLT: $1 ($2)"; failed=1; }; }

step "Werkzeuge"
need cargo "rustup / paket rust"
need restic "restic >= 0.17.1 oder RESTIC_BROWSER_TEST_RESTIC setzen"
need cargo-deny "cargo install --locked cargo-deny"
need cargo-audit "cargo install --locked cargo-audit"
need python3 "für scripts/third-party.py"
need xvfb-run "xorg-server-xvfb bzw. xvfb (für den Startversuch)"
if [ "$failed" -ne 0 ]; then echo "Fehlende Werkzeuge sind ein Fehler (AGENTS.md)."; fi
uname -a; cargo --version; restic version 2>/dev/null || true
echo "Sitzung: ${XDG_SESSION_TYPE:-unbekannt}"

export RESTIC_BROWSER_REQUIRE_RESTIC=1
run "cargo fmt --check" cargo fmt --check
run "cargo clippy" cargo clippy --workspace --all-targets --locked -- -D warnings
run "cargo test (Release, mit restic, inkl. unix-Tests)" cargo test --workspace --release --locked
run "cargo deny check licenses" cargo deny check licenses
run "cargo audit" cargo audit

step "Unix-spezifische Tests laufen wirklich"
cargo test --workspace --release --locked -- --list 2>/dev/null | grep -ciE "unix|symlink|permission|executable" || echo "Hinweis: keine Namen mit unix/symlink gefunden"

run "Release-Build" cargo build --release --locked -p restic-ui

step "Start unter Xvfb mit Demo-Repository (Debug-Build, Screenshot)"
if command -v xvfb-run >/dev/null 2>&1 && command -v restic >/dev/null 2>&1; then
  mkdir -p "$work/src/Dokumente" "$work/xdg"
  echo "Hallo Linux" > "$work/src/LIESMICH.txt"; echo "Bericht" > "$work/src/Dokumente/bericht.txt"
  ln -s LIESMICH.txt "$work/src/link.txt"
  export RESTIC_PASSWORD=linux-test RESTIC_REPOSITORY="$work/repo"
  # Unter Xvfb muss X11 verwendet werden; sonst öffnet winit bei gesetztem WAYLAND_DISPLAY ein Fenster auf dem echten Desktop.
  unset WAYLAND_DISPLAY; export XDG_SESSION_TYPE=x11
  restic init >/dev/null && restic backup "$work/src" --host linux-test >/dev/null && restic backup "$work/src" --host linux-test >/dev/null
  cargo build --locked -p restic-ui >/dev/null 2>&1
  for theme in "" "--dev-dark"; do
    out="$work/start${theme:+-dunkel}.png"
    XDG_DATA_HOME="$work/xdg" RESTIC_BROWSER_DEV_PASSWORD=linux-test timeout 120 xvfb-run --auto-servernum --server-args="-screen 0 1600x1000x24" \
      target/debug/restic-browser --dev-settings "$work/settings.json" --dev-repo "$work/repo" $theme --dev-screenshot "$out" \
      && [ -s "$out" ] && echo "OK: Screenshot $out" || { echo "FEHLER: Start/Screenshot ($theme)"; failed=1; }
    [ -s "$out" ] && cp "$out" "$root/dist-linux-check-$(basename "$out")" 2>/dev/null
  done
  XDG_DATA_HOME="$work/xdg" RESTIC_BROWSER_DEV_PASSWORD=linux-test timeout 120 xvfb-run --auto-servernum --server-args="-screen 0 1600x1000x24" \
    target/debug/restic-browser --dev-settings "$work/settings.json" --dev-repo "$work/repo" --dev-analysis --dev-screenshot "$work/analyse.png" \
    && [ -s "$work/analyse.png" ] && cp "$work/analyse.png" "$root/dist-linux-check-analyse.png" && echo "OK: Speicheranalyse" || { echo "FEHLER: Speicheranalyse"; failed=1; }
else
  echo "ÜBERSPRUNGEN: xvfb-run oder restic fehlt"; failed=1
fi

if [ "${1:-}" = "--package" ]; then
  run "Linux-Paket (benötigt pwsh, gpg, bzip2, Netzwerk)" sh "$root/scripts/package-linux.sh"
fi

echo
if [ "$failed" -eq 0 ]; then echo "ALLES OK"; else echo "ES GIBT FEHLER (siehe oben)"; fi
echo "Screenshots (falls erstellt): $root/dist-linux-check-*.png  (nicht committen)"
exit "$failed"
