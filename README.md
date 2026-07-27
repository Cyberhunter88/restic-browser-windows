# Restic Browser

Eine portable, deutschsprachige Oberfläche zum Durchsuchen und Wiederherstellen vorhandener [Restic](https://restic.net/)-Backups. Die Anwendung verändert keine Snapshots oder Repository-Daten und verwendet Restic als einzige Schnittstelle zum Repository.

## Downloads

[**Neueste portable ResticBrowser.exe herunterladen**](https://github.com/Cyberhunter88/restic-browser-windows/releases/latest/download/ResticBrowser.exe)

Alle veröffentlichten Versionen und Versionshinweise stehen unter [GitHub Releases](https://github.com/Cyberhunter88/restic-browser-windows/releases). Offizielle Windows-Binärdateien werden nach der Freischaltung durch SignPath digital signiert. Vertrauenswürdig sind ausschließlich Dateien aus diesem offiziellen GitHub-Repository und dessen offiziellen Releases.

## Plattformen und Funktionen

- Windows x64 als selbstständige `ResticBrowser.exe`
- Linux x64 als selbstständiges `ResticBrowser-linux-x64.tar.gz`
- lokale und entfernte Restic-Repositories, Snapshot-Filter, Suche und dateisystemartige Navigation
- Wiederherstellung einzelner oder mehrerer Dateien und Ordner mit Fortschritt und Abbruch
- Passwörter und Backend-Zugangsdaten nur im Arbeitsspeicher
- deutsche helle und dunkle Oberfläche

## Installation und Deinstallation

Restic Browser ist portabel und besitzt keinen Installer. Die Windows-EXE aus dem Release herunterladen oder das versionsbezogene ZIP-Archiv entpacken und starten. Für Linux das Archiv entpacken und `./ResticBrowser` starten. Zum Entfernen die Anwendung beziehungsweise den entpackten Programmordner löschen. Gespeicherte Profile bleiben erhalten; Passwörter werden nie gespeichert.

Restic wird nicht mitgeliefert. Es wird in dieser Reihenfolge gesucht:

1. neben der Anwendung (`restic.exe` unter Windows, `restic` unter Linux)
2. im Unterordner `tools`
3. über `PATH`
4. unter Windows zusätzlich in den üblichen WinGet-Pfaden

Für eine portable Nutzung Restic einfach neben die Anwendung oder in `tools` legen. Profilinformationen liegen unter Windows in `%LOCALAPPDATA%\ResticBrowser` und unter Linux in `$XDG_DATA_HOME/ResticBrowser` beziehungsweise `~/.local/share/ResticBrowser`.

## Linux-Voraussetzungen

Die Linux-Ausgabe enthält die .NET-Runtime, benötigt aber die üblichen Desktop-Grafikbibliotheken. Für Ubuntu 24.04 und Debian 13 müssen insbesondere `libgbm1`, `libgl1-mesa-dri`, `libegl1-mesa` und `libinput10` verfügbar sein. Avalonia verwendet unter Linux den X11-Pfad; auf Wayland-Desktops wird XWayland benötigt. Details stehen in den [Avalonia-Plattformanforderungen](https://docs.avaloniaui.net/docs/supported-platforms).

## Entwicklung und Prüfung

Voraussetzungen: .NET 10 SDK und für den End-to-End-Test Restic 0.17.1 oder neuer im `PATH` oder neben der App.

```powershell
dotnet build ResticBrowser.slnx -c Release
dotnet run --project tests/ResticBrowser.Tests/ResticBrowser.Tests.csproj -c Release --no-build
```

## Portable Ausgaben

Windows:

```powershell
./scripts/publish-windows.ps1
```

Das Ergebnis ist `dist/ResticBrowser.exe`.

Linux (unter Linux ausführen, damit das Ausführungsbit im Archiv erhalten bleibt):

```sh
chmod +x scripts/publish-linux.sh
./scripts/publish-linux.sh
```

Das Ergebnis ist `dist/ResticBrowser-linux-x64.tar.gz`. Das Archiv entpacken, `restic` bei Bedarf neben die Binärdatei oder unter `tools/restic` legen und `./ResticBrowser` starten.

## Privacy

Restic Browser erhebt oder übermittelt keine Telemetrie. Die Anwendung greift nur auf lokale oder entfernte Restic-Repositories und Speicherziele zu, die der Benutzer ausdrücklich auswählt oder konfiguriert. Bei entfernten Backends führt die separat installierte Restic-Anwendung die dafür erforderlichen Netzwerkzugriffe aus.

## Code signing policy

See the [Code Signing Policy](CODE_SIGNING_POLICY.md).

Free code signing provided by [SignPath.io](https://signpath.io/), certificate by [SignPath Foundation](https://signpath.org/).

## Security

Sicherheitsprobleme bitte vertraulich über [GitHubs private Sicherheitsmeldung](https://github.com/Cyberhunter88/restic-browser-windows/security/advisories/new) melden und nicht als öffentliches Issue veröffentlichen.

## Lizenz

Restic Browser steht unter der [MIT-Lizenz](LICENSE).
