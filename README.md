# Restic Browser

Eine portable, deutschsprachige Oberfläche zum Durchsuchen und Wiederherstellen vorhandener [Restic](https://restic.net/)-Backups. Die Anwendung verändert keine Snapshots oder Repository-Daten und verwendet Restic als einzige Schnittstelle zum Repository.

## Plattformen und Funktionen

- Windows x64 als selbstständige `ResticBrowser.exe`
- Linux x64 als selbstständiges `ResticBrowser-linux-x64.tar.gz`
- getestet auf Windows x64, Ubuntu 24.04 LTS x64 und Debian 13 x64
- lokale und entfernte Repositories, Snapshot-Filter, Suche und dateisystemartige Navigation
- Wiederherstellung einzelner oder mehrerer Dateien und Ordner mit Fortschritt und Abbruch
- Passwörter und Backend-Zugangsdaten nur im Arbeitsspeicher
- deutsche helle und dunkle Oberfläche

Restic wird nicht mitgeliefert. Es wird in dieser Reihenfolge gesucht:

1. neben der Anwendung (`restic.exe` unter Windows, `restic` unter Linux)
2. im Unterordner `tools`
3. über `PATH`
4. unter Windows zusätzlich in den üblichen WinGet-Pfaden

Für eine portable Nutzung Restic einfach neben die Anwendung oder in `tools` legen. Profilinformationen liegen unter Windows in `%LOCALAPPDATA%\ResticBrowser` und unter Linux in `$XDG_DATA_HOME/ResticBrowser` beziehungsweise `~/.local/share/ResticBrowser`. Passwörter werden nie gespeichert.

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
