# Restic Browser

Eine portable, deutschsprachige Oberfläche zum übersichtlichen Durchsuchen, Prüfen und Wiederherstellen vorhandener [Restic](https://restic.net/)-Backups. Restic bleibt die einzige Schnittstelle zum Repository. Die Anwendung arbeitet lesend und verändert weder Snapshots noch Repository-Daten.

Seit Version 4.0.0 ist Restic Browser in Rust geschrieben (Oberfläche mit [egui](https://github.com/emilk/egui)); die frühere C#-/Avalonia-Version wurde entfernt.

## Downloads

[**Neueste portable ResticBrowser.exe herunterladen**](https://github.com/Cyberhunter88/restic-browser-windows/releases/latest/download/ResticBrowser.exe)

[**Neueste portable Linux-x64-Version herunterladen**](https://github.com/Cyberhunter88/restic-browser-windows/releases/latest/download/ResticBrowser-linux-x64.tar.gz)

Alle veröffentlichten Versionen und Versionshinweise stehen unter [GitHub Releases](https://github.com/Cyberhunter88/restic-browser-windows/releases). Vertrauenswürdig sind ausschließlich Dateien aus diesem offiziellen GitHub-Repository und dessen offiziellen Releases. Die Windows-Binärdateien sind derzeit nicht digital signiert. Jedes Release enthält `SHA256SUMS.txt`.

## Plattformen und Funktionen

- Windows x64 als selbstständige `ResticBrowser.exe` mit eingebetteter, geprüfter Restic-Version
- Linux x64 als selbstständiges `ResticBrowser-linux-x64.tar.gz` einschließlich `tools/restic`
- lokale Repository-Ordner und Netzlaufwerke
- übersichtliche Snapshot-Auswahl mit einklappbaren Filtern für Host, Pfad, Tag und ID
- dateisystemartige Navigation, Suche im gewählten Snapshot und Suche nach der neuesten Dateiversion
- Dateivorschau (Text, Bilder) und Versionen je Datei
- lokale Wiederherstellung einzelner oder mehrerer Dateien und Ordner mit Fortschritt, Abbruch und unveränderlicher Vorschau
- „Neueste Version wiederherstellen“ für eine gesuchte Datei
- Speicheranalyse, Statistik (nur auf Anforderung) und lesende schnelle oder vollständige Integritätsprüfung
- Passwörter und Backend-Zugangsdaten nur im Arbeitsspeicher
- deutsche Oberfläche mit hellem und dunklem Design

Das Einbinden von Snapshots als virtuelles Laufwerk (Mount) gibt es seit 4.0.0 nicht mehr.
Frühere Versionen 3.x bleiben als GitHub Releases verfügbar.

## Bewusst nur lesender Zugriff

Restic Browser bietet keine Funktion zum Löschen, Bereinigen, Reparieren oder Erstellen
von Snapshots und Repository-Daten. Dadurch können beim Durchsuchen und
Wiederherstellen keine Sicherungen versehentlich verändert werden. Administrative
Restic-Befehle müssen bei Bedarf bewusst außerhalb der Anwendung ausgeführt werden.
Lesende Repository-Aufrufe verwenden `--no-lock`; während der Nutzung darf das Repository
nicht gleichzeitig extern bereinigt oder repariert werden.

Lesezeichen werden nicht gespeichert. Gespeicherte Profile enthalten weder Passwörter noch
Backend-Geheimnisse. Wiederherstellungsziele dürfen sich nicht mit einem lokalen Repository überlappen.

## Installation und Deinstallation

- **Portable Nutzung unter Windows**: `ResticBrowser.exe` herunterladen und direkt starten. Zum Entfernen einfach die Datei löschen.
- **Portable Nutzung unter Linux (x86_64)**: `ResticBrowser-linux-x64.tar.gz` herunterladen, in einen eigenen Ordner entpacken und die enthaltene Datei `ResticBrowser` starten. Zum Entfernen einfach den Ordner löschen.
- **Windows-Installation via Setup**: `ResticBrowser-Setup.exe` ausführen; Standardordner ist `C:\Program Files\Restic Browser` mit Startmenü-Verknüpfung. Die Deinstallation erfolgt über Apps & Features.

Gespeicherte Profile bleiben bei Deinstallation oder Aktualisierung erhalten. Sie liegen unter Windows in
`%LOCALAPPDATA%\ResticBrowser` und unter Linux in `$XDG_DATA_HOME/ResticBrowser` beziehungsweise
`~/.local/share/ResticBrowser`.

Restic 0.19.1 wird mitgeliefert und beim Build gegen die offizielle SHA-256-Prüfsumme sowie deren
OpenPGP-Signatur mit dem im Repository hinterlegten öffentlichen Restic-Schlüssel geprüft. Die
Windows-Einzeldatei stellt Restic bei Bedarf atomar und hash-geprüft im Benutzerdatenordner bereit;
das Linux-Archiv enthält `tools/restic`. Internet und Administratorrechte sind zur Nutzung nicht erforderlich.

Die Auswahl der Restic-Datei erfolgt in dieser Reihenfolge:

1. ausdrücklich im Verbindungsdialog ausgewählte Restic-Datei
2. geprüfte, mitgelieferte Version: unter Windows eingebettet, unter Linux aus `tools/restic`
3. portable Datei neben der Anwendung oder im Unterordner `tools`
4. `PATH`, unter Linux zusätzlich `/usr/local/bin/restic` und `/usr/bin/restic`,
   unter Windows zusätzlich die üblichen WinGet-Pfade

Jede tatsächlich gestartete Datei wird vor der Repository-Nutzung über `restic version --json`
geprüft; Dateien, die nicht ausführbar sind oder deren Prüfung nach fünf Sekunden nicht endet, werden
bei der automatischen Suche übersprungen. Eine ausdrücklich gewählte Datei wird bei einem Fehler nicht
durch ein anderes Programm ersetzt. Der Verbindungsdialog und die Kopfzeile zeigen die tatsächliche
Herkunft und Version. Es gibt keine Laufzeit-Downloads.

## Linux-Voraussetzungen

Das Linux-Archiv benötigt nur die üblichen Desktop-Grafikbibliotheken (X11 oder Wayland, OpenGL).
Unter Ubuntu bei Bedarf:

```sh
sudo apt update
sudo apt install libegl1 libgbm1 libgl1 libgl1-mesa-dri libxkbcommon0 libxkbcommon-x11-0 libwayland-client0
```

Unter aktuellem **Arch Linux x86_64**:

```sh
sudo pacman -Syu --needed gcc-libs libx11 libxcb libxkbcommon libxkbcommon-x11 libxrandr libxi libxcursor \
  wayland mesa libglvnd
```

Für den Ordnerdialog wird `xdg-desktop-portal` mit einem Backend des Desktops empfohlen.

**Programmsymbol unter Wayland (zum Beispiel KDE):** Wayland erlaubt Anwendungen nicht, ihr Fensterbild selbst zu setzen;
der Desktop sucht es über einen Anwendungsmenü-Eintrag. Ohne Eintrag zeigen Taskleiste und Titelleiste ein Standardsymbol.
Das Archiv enthält dafür ein optionales Skript, das nur im Benutzerbereich schreibt und sich wieder entfernen lässt:

```sh
sh install-desktop-entry.sh              # Eintrag und Symbol anlegen (Pfad der entpackten Anwendung)
sh install-desktop-entry.sh --entfernen  # wieder entfernen
```

Wird der Ordner verschoben, das Skript erneut ausführen. Unter X11 und Windows setzt die Anwendung das Symbol selbst.
Diese Pakete werden nicht automatisch installiert. Danach entpacken und starten:

```sh
mkdir -p "$HOME/Restic Browser"
tar -xzf ResticBrowser-linux-x64.tar.gz -C "$HOME/Restic Browser"
"$HOME/Restic Browser/ResticBrowser"
```

Eine systemweite Restic-Installation ist nicht erforderlich. ARM und Arch-basierte Derivate werden
nicht separat geprüft.

## Entwicklung und Prüfung

Voraussetzungen: aktuelle stabile Rust-Toolchain (`rustup`), Python 3. Für lokale Integrationstests
Restic 0.17.1 oder neuer im `PATH` oder in `RESTIC_BROWSER_TEST_RESTIC`; für Prüfungen außerdem
`cargo-deny` und `cargo-audit` (`cargo install --locked cargo-deny cargo-audit`).
Der Cargo-Workspace liegt in `rust/`:

```sh
cd rust
cargo fmt --check
cargo clippy --workspace --all-targets --locked -- -D warnings
RESTIC_BROWSER_REQUIRE_RESTIC=1 cargo test --workspace --release --locked
cargo deny check licenses
cargo audit
```

Unter Linux prüft `sh rust/scripts/linux-verify.sh [--package]` die gesamte Kette einschließlich
Xvfb-Start und Paket. Aufbau, Entscheidungen und Testzuordnung stehen in `docs/architecture.md`,
`docs/decisions.md` und `docs/rust-migration.md`.

## Versionen und Releases

`version` unter `[workspace.package]` in `rust/Cargo.toml` ist die zentrale Versionsquelle
(SemVer `MAJOR.MINOR.PATCH`); `rust/Cargo.lock` wird im selben Pull Request mitgeführt. Funktionale
Änderungen erhöhen die Version im selben Pull Request nach Semantic Versioning. Die CI läuft für
Pull Requests nach `main`, Pushes auf `main` und manuelle Starts.

Releases werden bewusst manuell angefordert:

1. Auf GitHub **Actions → Release starten → Run workflow** öffnen und `main` auswählen.
2. Unter **version** die gewünschte Version eingeben, zum Beispiel `4.0.1` oder `v4.0.1`.
3. Ist die Version höher als die aktuelle Produktversion, erstellt Actions einen PR,
   der ausschließlich `rust/Cargo.toml` und `rust/Cargo.lock` aktualisiert, und startet dessen CI.
4. Den Versions-PR nach erfolgreicher CI mergen. Erst nach erfolgreicher Main-CI
   baut und prüft der Release-Workflow die Pakete und veröffentlicht automatisch
   `vX.Y.Z` als neuesten GitHub Release.

Entspricht die Eingabe bereits der Produktversion auf `main` (so bei 4.0.0), startet die
Release-Prüfung direkt für diesen Commit, sofern dessen Main-CI erfolgreich ist. Bereits vorhandene
Tags müssen auf denselben Commit zeigen; vorhandene Assets werden geprüft und nicht überschrieben.
Ältere Versionen, Vorabversionen und Starts auf anderen Branches werden abgewiesen.

Normale Merges und Versionsänderungen veröffentlichen nichts. Tags und Releases werden
ausschließlich im Release-Workflow erstellt. Die Repository-Einstellung **Settings → Actions →
General → Workflow permissions → Allow GitHub Actions to create and approve pull requests**
muss aktiviert sein.

## Ausgaben und Installer

Die Pakete entstehen in `dist-rust/`:

```powershell
# Windows (benötigt GnuPG, Python 3; mit -Installer zusätzlich Inno Setup 6)
./rust/scripts/package-windows.ps1 [-Installer]
```

```sh
# Linux (benötigt pwsh, gpg, bzip2, python3; unter Linux ausführen, damit das Ausführungsbit erhalten bleibt)
sh rust/scripts/package-linux.sh
```

Ergebnisse: `ResticBrowser.exe`, `ResticBrowserWindows-<Version>-win-x64.zip`, `ResticBrowser-Setup.exe`
und `ResticBrowser-linux-x64.tar.gz`, jeweils mit `THIRD-PARTY.md` (Drittkomponenten und Lizenzen).
Die CI prüft das Linux-Archiv auf Ubuntu und in einem aktuellen `archlinux:base`-Container
(`scripts/verify-linux-package.sh`, `scripts/verify-arch-linux-package.sh`): Ausführungsrechte,
gebündelte Restic-Version und GUI-Start unter Xvfb als unprivilegierter Benutzer aus einem Pfad mit
Leerzeichen. Das ersetzt keine manuelle Prüfung von Dialogen, Restore oder Wayland.

## Privacy

Restic Browser erhebt oder übermittelt keine Telemetrie.
Die Anwendung greift nur auf Restic-Repositories zu, die der Benutzer ausdrücklich auswählt.

## Security

Sicherheitsprobleme bitte vertraulich über [GitHubs private Sicherheitsmeldung](https://github.com/Cyberhunter88/restic-browser-windows/security/advisories/new) melden und nicht als öffentliches Issue veröffentlichen.

## Lizenz

Restic Browser steht unter der [Apache-Lizenz 2.0](LICENSE). Verwendete Drittkomponenten und ihre
Lizenzen sind in `THIRD-PARTY.md` des jeweiligen Pakets aufgeführt.
