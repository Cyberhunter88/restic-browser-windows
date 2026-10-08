# Restic Browser

Eine portable, deutschsprachige Oberfläche zum übersichtlichen Durchsuchen, Prüfen und Wiederherstellen vorhandener [Restic](https://restic.net/)-Backups. Restic bleibt die einzige Schnittstelle zum Repository. Die Anwendung arbeitet lesend und verändert weder Snapshots noch Repository-Daten.

## Downloads

[**Neueste portable ResticBrowser.exe herunterladen**](https://github.com/Cyberhunter88/restic-browser-windows/releases/latest/download/ResticBrowser.exe)

[**Neueste portable Linux-x64-Version herunterladen**](https://github.com/Cyberhunter88/restic-browser-windows/releases/latest/download/ResticBrowser-linux-x64.tar.gz)

Alle veröffentlichten Versionen und Versionshinweise stehen unter [GitHub Releases](https://github.com/Cyberhunter88/restic-browser-windows/releases). Vertrauenswürdig sind ausschließlich Dateien aus diesem offiziellen GitHub-Repository und dessen offiziellen Releases. Die Windows-Binärdateien sind derzeit nicht digital signiert.

## Plattformen und Funktionen

- Windows x64 als selbstständige `ResticBrowser.exe` mit geprüfter Restic-Version
- Linux x64 als selbstständiges `ResticBrowser-linux-x64.tar.gz` einschließlich `tools/restic`
- lokale Repository-Ordner und Netzlaufwerke
- übersichtliche Snapshot-Auswahl mit einklappbaren Filtern für Host, Pfad, Tag und ID
- dateisystemartige Navigation, Suche im gewählten Snapshot und Suche nach der neuesten Dateiversion
- Snapshots und Suchtreffer erscheinen bereits während des Ladens; die fertige Snapshot-Liste wird nach Zeitpunkt sortiert
- Dateivorschau und Versionen je Datei
- lokale Wiederherstellung einzelner oder mehrerer Dateien und Ordner mit Fortschritt, Abbruch und unveränderlicher Vorschau
- Speicheranalyse
- lesende schnelle oder vollständige Integritätsprüfung des Repositorys
- Einbinden von Snapshots als virtuelles Laufwerk unter Linux
- Passwörter und Backend-Zugangsdaten nur im Arbeitsspeicher
- aufgeräumte deutsche Oberfläche mit hellem und dunklem Design

## Änderungen in Version 3.0.0

REST-Verbindungen sind ebenfalls entfernt. Gespeicherte REST-Profile werden beim Laden
gelöscht; lokale Profile bleiben erhalten. Die Verbindungstyp-Auswahl entfällt.
Das Restic-Feld zeigt bei automatischer Auswahl einen sichtbaren Hinweis; nach einer
Prüfung werden die tatsächliche Herkunft, Version und der Programmpfad angezeigt.

## Änderungen in Version 2.0.0

SFTP, S3/MinIO, Textversionsvergleich, Snapshot-Vergleich und Sitzungsdiagnose
wurden entfernt. Beim ersten Laden werden gespeicherte SFTP- und S3-Profile aus
der Profildatei gelöscht. Lokale und REST-Profile bleiben erhalten.

## Bewusst nur lesender Zugriff

Restic Browser bietet keine Funktion zum Löschen, Bereinigen, Reparieren oder Erstellen
von Snapshots und Repository-Daten. Dadurch können beim Durchsuchen und
Wiederherstellen keine Sicherungen versehentlich verändert werden. Administrative
Restic-Befehle müssen bei Bedarf bewusst außerhalb der Anwendung ausgeführt werden.

Lesezeichen werden nicht gespeichert. Die Navigation konzentriert sich auf die
Snapshot-Auswahl, den aktuellen Pfad und die Vorwärts-/Zurück-Navigation innerhalb
der laufenden Sitzung.

## Installation und Deinstallation

Restic Browser steht als portable Anwendung für Windows und Linux sowie optional als Windows-Installer zur Verfügung:

- **Portable Nutzung unter Windows**: `ResticBrowser.exe` herunterladen und direkt starten. Zum Entfernen einfach die Datei oder den Ordner löschen.
- **Portable Nutzung unter Linux (Ubuntu und Arch Linux x86_64)**: `ResticBrowser-linux-x64.tar.gz` herunterladen, in einen eigenen Ordner entpacken und die enthaltene Datei `ResticBrowser` direkt starten. Zum Entfernen einfach den Ordner löschen.
- **Windows-Installation via Setup**: `ResticBrowser-Setup.exe` ausführen, um die Anwendung im Standard-Programmordner (`C:\Program Files\Restic Browser`) mit Startmenü-Verknüpfung zu installieren. Die Deinstallation erfolgt sauber über die Windows-Systemsteuerung (Apps & Features).

Gespeicherte Profile bleiben bei Deinstallation oder Aktualisierung erhalten; Passwörter werden nie gespeichert.

Restic 0.19.1 wird für die portable Ausgabe mitgeliefert und beim Build gegen die offizielle
SHA-256-Prüfsumme sowie deren OpenPGP-Signatur mit dem im Repository hinterlegten öffentlichen
Restic-Schlüssel geprüft. Die Windows-Einzeldatei entpackt Restic bei Bedarf hash-geprüft in den
Benutzerdatenordner; das Linux-Archiv enthält `tools/restic`. Internet und Administratorrechte
sind zur Nutzung nicht erforderlich.

Die Auswahl erfolgt in dieser Reihenfolge:

1. ausdrücklich im Verbindungsdialog ausgewählte Restic-Datei
2. geprüfte, mitgelieferte Version: unter Windows eingebettet und beim ersten Bedarf bereitgestellt,
   unter Linux aus `tools/restic`
3. portable Datei neben der Anwendung oder im Unterordner `tools`
4. `PATH`, unter Linux zusätzlich `/usr/local/bin/restic` und `/usr/bin/restic`,
   unter Windows zusätzlich die üblichen WinGet-Pfade

Die mitgelieferte Datei wird vor der Ausführung per SHA-256 und anschließend immer über
`restic version --json` geprüft. Es gibt keine Laufzeit-Downloads. Eine manuell ausgewählte
externe Datei bleibt als bewusste Kompatibilitäts- oder Testoption erhalten. Windows legt die
geprüfte Ressource erst beim Verbinden im Benutzerdatenordner ab; temporäre Dateien und eine
Provisionierungs-Sperre verhindern beschädigte parallele Installationen. Profilinformationen
liegen unter Windows in `%LOCALAPPDATA%\ResticBrowser` und unter Linux in
`$XDG_DATA_HOME/ResticBrowser` beziehungsweise `~/.local/share/ResticBrowser`.

Die automatische Suche überspringt Dateien, die nicht ausführbar sind, die Versionsprüfung
nicht bestehen oder deren Prüfung nach fünf Sekunden abgebrochen wird. Eine ausdrücklich
gewählte Datei wird bei einem Fehler nicht durch ein anderes Programm ersetzt. Der
Verbindungsdialog zeigt nach erfolgreicher Prüfung die tatsächliche Herkunft und Version;
die automatische Auswahl wird nicht dauerhaft gespeichert.

Das Hauptfenster passt sich unter 1100 logischen Pixeln Breite an: Die Snapshot-Spalte wird
schmaler, das Suchfeld erhält eine eigene Zeile und die Dateitabelle zeigt die wesentlichen
Spalten. Die Werkzeugleiste bricht bei Platzmangel um. Die Skalierung des Desktops bleibt
maßgeblich; bei größeren Fenstern erscheint auch die Berechtigungsspalte wieder.

## Linux-Voraussetzungen

Die Linux-Ausgabe enthält die .NET-Runtime, benötigt aber die üblichen Desktop-Grafikbibliotheken. Unter Ubuntu können diese bei Bedarf mit folgendem Befehl installiert werden:

```sh
sudo apt update
sudo apt install libegl1 libgbm1 libgl1 libgl1-mesa-dri libinput10
```

Unter aktuellem **Arch Linux x86_64** wird dasselbe portable Archiv verwendet. Die Desktop-
und Runtime-Systembibliotheken lassen sich bei Bedarf mit einer vollständigen Aktualisierung installieren:

```sh
sudo pacman -Syu --needed ca-certificates icu krb5 gcc-libs libunwind openssl zlib \
  libx11 libice libsm libxrandr libxi libxcursor fontconfig freetype2 \
  mesa libglvnd libinput ttf-dejavu
```

Auf einem Wayland-Desktop zusätzlich `sudo pacman -Syu --needed xorg-xwayland` ausführen.
Für das optionale Einbinden von Snapshots wird `fuse3` benötigt
(`sudo pacman -Syu --needed fuse3`).
Diese Pakete werden von der Anwendung nicht automatisch installiert.

Danach das heruntergeladene Archiv in einen eigenen Ordner entpacken und starten:

```sh
mkdir -p "$HOME/Restic Browser"
tar -xzf ResticBrowser-linux-x64.tar.gz -C "$HOME/Restic Browser"
"$HOME/Restic Browser/ResticBrowser"
```

.NET, PowerShell und eine systemweite Restic-Installation sind für die Nutzung nicht erforderlich.
Das Archiv benötigt einen beschreibbaren Benutzerdaten-/Cache-Bereich zur Speicherung von
Profilen und zum Entpacken der eingebetteten Runtime-Bibliotheken. ARM und Arch-basierte
Derivate werden nicht separat geprüft. PowerShell wird nur beim Erstellen des Pakets benötigt.

Avalonia verwendet unter Linux den X11-Pfad; auf Wayland-Desktops wird XWayland benötigt. Details stehen in den [Avalonia-Plattformanforderungen](https://docs.avaloniaui.net/docs/supported-platforms).

Das Einbinden eines Snapshots als virtuelles Laufwerk ist in Restic Browser ausschließlich unter Linux verfügbar. Dafür müssen FUSE (`/dev/fuse`) und `fusermount3` (oder `fusermount`) installiert und verfügbar sein. Der Mount-Pfad muss leer sein und darf sich nicht mit einem lokalen Repository überlappen. Unter Windows stehen Dateivorschau und gezielte Wiederherstellung zur Verfügung.

## Entwicklung und Prüfung

Voraussetzungen: .NET 10 SDK. Für lokale Integrationstests kann zusätzlich Restic 0.17.1 oder
neuer im `PATH` liegen; die veröffentlichte portable Anwendung verwendet die geprüfte
mitgelieferte Datei.

```powershell
dotnet build ResticBrowser.slnx -c Release
dotnet run --project tests/ResticBrowser.Tests/ResticBrowser.Tests.csproj -c Release --no-build
```

## Versionen und Releases

`ResticBrowserProductVersion` in `Directory.Build.props` ist die zentrale Versionsquelle
und enthält eine SemVer-Version im Format `MAJOR.MINOR.PATCH`. Funktionale Änderungen erhöhen
die Version im selben Pull Request nach Semantic Versioning. Die CI läuft für
Pull Requests nach `main`, Pushes auf `main` und manuelle Starts.

Wenn sich die Produktversion in `Directory.Build.props` auf `main` ändert, baut GitHub Actions die Windows- und
Linux-Artefakte, prüft sie und erstellt anschließend den passenden Tag
`vX.Y.Z` sowie den GitHub Release mit automatisch erzeugten Release Notes.
Ohne Versionsänderung läuft nur die normale CI. Tags und Releases werden nicht
manuell erstellt oder verändert.

## Ausgaben und Installer

Windows Portable:

```powershell
./scripts/publish-windows.ps1
```

Das Ergebnis ist `dist/ResticBrowser.exe`.

Windows Installer (benötigt [Inno Setup](https://jrsoftware.org/isinfo.php)):

```powershell
./scripts/publish-windows-installer.ps1
```

Das Ergebnis ist `dist/ResticBrowser-Setup.exe`.

Linux x64 (Ubuntu/Arch; unter Linux ausführen, damit das Ausführungsbit im Archiv erhalten bleibt):

```sh
chmod +x scripts/publish-linux.sh
./scripts/publish-linux.sh
```

Das Ergebnis ist `dist/ResticBrowser-linux-x64.tar.gz`. Das Archiv enthält die ausführbare Datei
`ResticBrowser`, `tools/restic`, `README.md` und `LICENSE`:

```sh
mkdir ResticBrowser-linux-x64
tar -xzf ResticBrowser-linux-x64.tar.gz -C ResticBrowser-linux-x64
cd ResticBrowser-linux-x64
./ResticBrowser
```

Die CI prüft das auf Ubuntu gebaute Archiv zusätzlich in einem aktuellen `archlinux:base`-
Container ohne systemweites .NET oder Restic. Beide Plattformprüfungen verwenden
`bash scripts/verify-linux-package.sh dist/ResticBrowser-linux-x64.tar.gz`; der Arch-Test
wird über `bash scripts/verify-arch-linux-package.sh dist/ResticBrowser-linux-x64.tar.gz`
ausgeführt und benötigt Docker sowie Netzwerk für Container und Systempakete.
Er prüft Ausführungsrechte, die gebündelte Restic-Version und einen GUI-Start unter Xvfb
als unprivilegierter Benutzer, aus einem Pfad mit Leerzeichen und einem anderen Arbeitsverzeichnis.
Dieser Starttest ersetzt keine manuelle Prüfung von Themes, Dialogen, Restore oder Wayland.

Eine .NET-Installation ist für die portable Ausgabe nicht erforderlich. Eine alternative Restic-
Datei kann im Verbindungsdialog ausdrücklich ausgewählt werden.

## Privacy

Restic Browser erhebt oder übermittelt keine Telemetrie.
Die Anwendung greift nur auf lokale oder entfernte Restic-Repositories und Speicherziele zu,
die der Benutzer ausdrücklich auswählt oder konfiguriert.

## Security

Sicherheitsprobleme bitte vertraulich über [GitHubs private Sicherheitsmeldung](https://github.com/Cyberhunter88/restic-browser-windows/security/advisories/new) melden und nicht als öffentliches Issue veröffentlichen.

## Lizenz

Restic Browser steht unter der [MIT-Lizenz](LICENSE).
