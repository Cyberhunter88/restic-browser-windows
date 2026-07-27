# AGENTS.md

## Projekt

Restic Browser ist eine deutschsprachige, portable Avalonia-Anwendung für Windows und Linux.
Sie durchsucht vorhandene Restic-Repositories und stellt ausgewählte Dateien oder
Ordner wieder her. Restic bleibt die einzige Schnittstelle zum Repository.

## Technische Leitlinien

- Zielplattform: `net10.0`, Avalonia 12.1, `win-x64` und `linux-x64`.
- Keine Funktionen implementieren, die Snapshots oder Repository-Daten löschen,
  verändern, bereinigen oder neu anlegen.
- Restic-Prozesse immer ohne Shell über `ProcessStartInfo.ArgumentList` starten.
- Passwörter und Backend-Geheimnisse ausschließlich in der Prozessumgebung und im
  Arbeitsspeicher halten. Niemals protokollieren oder in Einstellungen speichern.
- JSON-Ausgaben tolerant gegen zusätzliche Felder und unbekannte Nachrichtentypen
  verarbeiten.
- Alle sichtbaren Texte und verständlichen Fehlermeldungen bleiben auf Deutsch.
- Helles und dunkles Design müssen für Avalonia-Controls und Dialoge funktionieren.
- Die portable Restic-Suche neben der App, im Unterordner `tools` und über `PATH`
  beibehalten; WinGet-Pfade gelten zusätzlich nur unter Windows.

## Oberfläche und Themes

- Farben ausschließlich über dynamische Ressourcen in `App.axaml` und
  `App.SetTheme` definieren. Keine fest eingebauten hellen Systemfarben verwenden.
- Neue oder geänderte Controls in Hell und Dunkel prüfen. Das umfasst Normal,
  Hover, Pressed, Fokus, Auswahl, Disabled, Ladezustand und geöffnete Popups.
- Avalonia-Themes insbesondere für `DataGrid`, `ComboBox`, Listen, Eingabefelder
  und Tooltips nicht ungeprüft übernehmen.
- Theme-konforme Spaltengrößenanfasser unsichtbar halten, ihre Bedienbarkeit aber
  nicht entfernen.
- Animationen bleiben dezent und funktional: normalerweise 100–200 ms,
  Ease-Out, keine dauernden dekorativen Bewegungen. Endlosanimationen nur für
  echte Ladezustände verwenden.
- Fenster und Dialoge müssen dieselbe dunkle Titelleiste, Hintergrundfarbe,
  Abstände und Fokusdarstellung verwenden.
- Nach visuellen Änderungen Hauptfenster und Verbindungsdialog in beiden Themes
  direkt erfassen. Interaktionsfehler zusätzlich mit simuliertem Hover prüfen.

## App-Icon

- `Assets/app.ico` ist das Windows-EXE- und Fenster-Icon.
- `Assets/app-icon.png` ist die hochauflösende Darstellung im App-Header.
- Beide Dateien sowie `ApplicationIcon` und die Avalonia-Resource-Einträge im Projekt
  gemeinsam aktualisieren; das eingebettete Windows-EXE-Icon nach dem Build kontrollieren.

## Prüfen

```powershell
dotnet build ResticBrowser.slnx -c Release
dotnet run --project tests/ResticBrowser.Tests/ResticBrowser.Tests.csproj -c Release --no-build
./scripts/publish-windows.ps1
./scripts/publish-windows-installer.ps1
# unter Linux:
./scripts/publish-linux.sh
```

Der Test-Runner enthält einen lokalen End-to-End-Test, der ein temporäres
Restic-Repository erstellt und anschließend vollständig entfernt.

## Releases

- Vor jedem Release die semantische Version in `ResticBrowser.csproj` konsistent
  für `Version`, `AssemblyVersion`, `FileVersion` und `InformationalVersion` erhöhen.
- Nur einen sauberen, getesteten `main`-Stand taggen und veröffentlichen.
- `dist/ResticBrowser.exe` als exakt benanntes Windows-GitHub-Release-Asset hochladen,
  damit der stabile Link
  `releases/latest/download/ResticBrowser.exe` weiterhin funktioniert.
- Zusätzlich `dist/ResticBrowser-linux-x64.tar.gz` als Linux-Release-Asset hochladen.
- SHA-256 vor dem Upload erfassen. Das veröffentlichte Asset anschließend erneut
  herunterladen und seinen Hash mit dem lokalen geprüften Build vergleichen.
- Releases als `latest`, nicht als Draft oder Prerelease veröffentlichen, sofern
  der Benutzer nichts anderes verlangt.
