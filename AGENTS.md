# AGENTS.md

## Projekt

Restic Browser ist eine deutschsprachige, portable WPF-Anwendung für Windows.
Sie durchsucht vorhandene Restic-Repositories und stellt ausgewählte Dateien oder
Ordner wieder her. Restic bleibt die einzige Schnittstelle zum Repository.

## Technische Leitlinien

- Zielplattform: `net10.0-windows`, WPF, `win-x64`.
- Keine Funktionen implementieren, die Snapshots oder Repository-Daten löschen,
  verändern, bereinigen oder neu anlegen.
- Restic-Prozesse immer ohne Shell über `ProcessStartInfo.ArgumentList` starten.
- Passwörter und Backend-Geheimnisse ausschließlich in der Prozessumgebung und im
  Arbeitsspeicher halten. Niemals protokollieren oder in Einstellungen speichern.
- JSON-Ausgaben tolerant gegen zusätzliche Felder und unbekannte Nachrichtentypen
  verarbeiten.
- Alle sichtbaren Texte und verständlichen Fehlermeldungen bleiben auf Deutsch.
- Helles und dunkles Design müssen für native WPF-Controls und Dialoge funktionieren.
- Die portable Restic-Suche neben der App, im Unterordner `tools`, über `PATH` und
  über WinGet-Pfade beibehalten.

## Prüfen

```powershell
dotnet build ResticBrowser.slnx -c Debug
dotnet run --project tests/ResticBrowser.Tests/ResticBrowser.Tests.csproj --no-build
dotnet publish src/ResticBrowser/ResticBrowser.csproj -c Release -r win-x64 --self-contained true
```

Der Test-Runner enthält einen lokalen End-to-End-Test, der ein temporäres
Restic-Repository erstellt und anschließend vollständig entfernt.
