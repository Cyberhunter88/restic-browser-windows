# Linux-Abnahme der Rust-Version (CachyOS/Arch, auch Debian/Ubuntu)

Ziel: die Rust-Anwendung (`rust/`, Branch `codex/rust-migration`) unter Linux prüfen. Sie wurde bisher nur unter
Windows gebaut und getestet; Linux-Code (`#[cfg(unix)]`) ist nur kompiliert.

## Voraussetzungen

- Branch auf den Linux-Rechner bringen: `git fetch origin && git switch codex/rust-migration && git pull --ff-only`
  (der Branch muss dafür gepusht sein; alternativ `git bundle`). Arbeitsbaum vorher prüfen.
- Pakete (Arch): `rust` oder `rustup`, `restic`, `python`, `xorg-server-xvfb`, `mesa` (Software-GL für Xvfb),
  `libxkbcommon`, `wayland`, `xdg-desktop-portal` (Ordnerdialog), optional `powershell`, `gnupg`, `bzip2` für das Paket.
  Debian/Ubuntu: `xvfb libgl1-mesa-dri libxkbcommon0 libwayland-client0 xdg-desktop-portal`.
- Cargo-Werkzeuge: `cargo install --locked cargo-deny cargo-audit`.
- restic ≥ 0.17.1 (gebündelt ist 0.19.1); sonst `RESTIC_BROWSER_TEST_RESTIC=/pfad/zu/restic` setzen.

## Automatische Prüfung

```sh
sh rust/scripts/linux-verify.sh            # Werkzeuge, fmt, clippy, Tests, deny, audit, Release-Build, Xvfb-Start
sh rust/scripts/linux-verify.sh --package  # zusätzlich das Linux-Paket (pwsh, gpg, bzip2, Netzwerk)
```

Das Skript nutzt nur temporäre Verzeichnisse, legt ein Demo-Repository mit Symlink an, startet die Oberfläche
unter Xvfb (hell und dunkel, plus Speicheranalyse) und legt Screenshots als `dist-linux-check-*.png` ab
(in `.gitignore`, nicht committen). Exit-Code 0 = alles in Ordnung.

## Manuelle Prüfung (mit echter Sitzung, X11 und Wayland wenn möglich)

1. Start ohne Optionen: Fenster erscheint, Verbinden-Dialog, Ordnerauswahl für Repository und Ziel (Portal-Dialog).
2. Verbinden mit einem lokalen Repository; Herkunft der restic-Datei in der Kopfzeile (System-`PATH` oder `tools/restic`
   neben der Anwendung nach dem Paketbau).
3. Navigation, Suche, Mehrfachauswahl, Kontextmenü (Rechtsklick), Vorschau, Dateiversionen.
4. Wiederherstellen: Symlink, Dateirechte und Zeiten im Ziel kontrollieren; Zielordner ohne Schreibrecht ergibt eine
   deutsche Fehlermeldung ohne Reste.
5. „Neueste Version wiederherstellen …“, Prüfung, Statistik, Speicheranalyse.
6. Fenstergröße/-position: schließen, neu starten, Werte bleiben (nur mit echtem Start, nicht `--dev-*`).
7. Hell/Dunkel; HiDPI (z. B. `WINIT_X11_SCALE_FACTOR=1.5`); Fenstersymbol sichtbar.
8. Mount gibt es nicht mehr (seit 4.0.0 entfernt).

## Prompt für die Linux-Codex-Sitzung

```text
Lies zuerst AGENTS.md, STATUS.md, PROJECT-STATE.md, docs/rust-migration.md, docs/rust-parity.md und
docs/rust-linux-test.md und prüfe `git status --short --branch`. Wir arbeiten auf dem Branch
codex/rust-migration (Rust-Migration von Restic Browser, Code unter rust/). Es wurde bisher nur unter Windows
getestet.

Aufgabe: die Rust-Version unter Linux abnehmen und Fehler beheben.
1. Fehlende Werkzeuge installieren (cargo-deny, cargo-audit, xvfb, mesa, restic >= 0.17.1) – fehlende
   Prüfwerkzeuge sind ein Fehler. Danach `sh rust/scripts/linux-verify.sh` ausführen.
2. Jeden Fehler ursächlich beheben (kein Überspringen, kein --no-verify, keine abgeschwächten Tests). Besonders
   achten auf: #[cfg(unix)]-Tests (Symlinks, Rechte, Prozessabbruch), Restore von Symlinks, tools/restic neben der
   Anwendung, Ordnerdialog (rfd/xdg-portal), Start unter X11 und Wayland, Hell/Dunkel, HiDPI.
3. Screenshots (dist-linux-check-*.png) ansehen und Darstellungsfehler (Schrift, Umlaute, Icons, Tabellen,
   Dialoge) beheben; nichts davon committen.
4. Danach `sh rust/scripts/linux-verify.sh --package` und das Paket (tar.gz) entpacken und prüfen:
   ResticBrowser startet, tools/restic wird als "mitgeliefert" erkannt, THIRD-PARTY.md ist enthalten.
5. Manuelle Punkte aus docs/rust-linux-test.md soweit möglich durchgehen (Kontextmenü, Neueste Version
   wiederherstellen, Fenstergröße speichern). Für Nicht-Prüfbares klar festhalten, warum.
6. Ergänzungen vornehmen: docs/rust-parity.md (Linux-Zeile und Nachweise), STATUS.md, docs/rust-migration.md
   (Abschnitt „Stand Linux-Abnahme“ mit Befunden und Korrekturen). Fixes klein halten, Fehlermeldungen deutsch,
   nur lesender Repository-Zugriff, kein Mount, keine neuen unsafe-Blöcke.
Regeln: Änderungen gezielt stagen und committen (Branch codex/rust-migration), cargo fmt, clippy -D warnings,
cargo test --release, cargo deny/audit vor jedem Commit; nicht pushen, keine Tags/Releases, keine Secrets, keine
Nutzerpfade in Doku. Abschlussbericht: was geprüft, was behoben, was offen ist, plus Ausgabe von
`git log --oneline origin/main..HEAD`.
```
