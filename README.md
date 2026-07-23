# Restic Browser

Eine portable Windows-Oberfläche zum Durchsuchen und Wiederherstellen vorhandener
[Restic](https://restic.net/)-Backups. Die Anwendung verändert keine Snapshots und
verwendet eine bereits installierte `restic.exe` ab Version 0.17.1.

## Funktionen

- lokale und entfernte Restic-Repositories
- Snapshot-Filter und dateisystemartige Navigation
- Suche im ausgewählten Snapshot
- Wiederherstellung einzelner oder mehrerer Dateien und Ordner
- vier sichere Überschreibmodi mit Fortschrittsanzeige und Abbruch
- Passwörter und Backend-Zugangsdaten nur im Arbeitsspeicher
- helle und dunkle deutsche Oberfläche

## Nutzung vom USB-Stick

`ResticBrowser.exe` ist selbstständig und kann direkt von einem USB-Stick gestartet
werden. Auf dem Zielrechner ist keine .NET-Runtime erforderlich.

Restic wird in dieser Reihenfolge automatisch gesucht:

1. `restic.exe` neben `ResticBrowser.exe`
2. `tools\restic.exe` neben der Anwendung
3. Einträge aus `PATH`
4. typische WinGet-Installationspfade

Für einen vollständig portablen Stick einfach `ResticBrowser.exe` und eine passende
Windows-x64-Version von `restic.exe` in denselben Ordner kopieren. Restic selbst wird
nicht mit dem Release gebündelt. Gespeicherte Repository-Profile liegen weiterhin
unter `%LOCALAPPDATA%\ResticBrowser` auf dem jeweiligen PC; Passwörter werden nie
gespeichert.

## Entwicklung

Voraussetzungen: Windows, .NET 10 SDK und optional Restic 0.17.1 oder neuer.

```powershell
dotnet build ResticBrowser.slnx
dotnet run --project tests/ResticBrowser.Tests
```

## Portable Einzeldatei

```powershell
dotnet publish src/ResticBrowser/ResticBrowser.csproj -c Release -r win-x64 --self-contained true
```

Die EXE liegt anschließend unter
`src\ResticBrowser\bin\Release\net10.0-windows\win-x64\publish\ResticBrowser.exe`.

Repository-Profile werden unter `%LOCALAPPDATA%\ResticBrowser\settings.json`
gespeichert. Passwörter und Umgebungsvariablen werden niemals dort abgelegt.
