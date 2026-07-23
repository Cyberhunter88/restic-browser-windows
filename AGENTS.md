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

## Oberfläche und Themes

- Farben ausschließlich über dynamische Ressourcen in `App.xaml` und
  `App.SetTheme` definieren. Keine fest eingebauten hellen Systemfarben verwenden.
- Neue oder geänderte Controls in Hell und Dunkel prüfen. Das umfasst Normal,
  Hover, Pressed, Fokus, Auswahl, Disabled, Ladezustand und geöffnete Popups.
- Native WPF-Templates insbesondere für `GridViewColumnHeader`,
  `DataGridColumnHeader`, `ComboBox`, Listen, Eingabefelder und Tooltips nicht
  ungeprüft übernehmen. Windows-Standardtemplates können im Dark Mode helle
  Verläufe oder Größenanfasser anzeigen.
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

- `Assets/app.ico` ist das EXE-, Fenster- und Taskleisten-Icon.
- `Assets/app-icon.png` ist die hochauflösende Darstellung im App-Header.
- Beide Dateien sowie `ApplicationIcon` und die WPF-Resource-Einträge im Projekt
  gemeinsam aktualisieren; das eingebettete EXE-Icon nach dem Build kontrollieren.

## Prüfen

```powershell
dotnet build ResticBrowser.slnx -c Release
dotnet run --project tests/ResticBrowser.Tests/ResticBrowser.Tests.csproj -c Release --no-build
dotnet publish src/ResticBrowser/ResticBrowser.csproj -c Release -r win-x64 --self-contained true -o dist
```

Der Test-Runner enthält einen lokalen End-to-End-Test, der ein temporäres
Restic-Repository erstellt und anschließend vollständig entfernt.

## Releases

- Vor jedem Release die semantische Version in `ResticBrowser.csproj` konsistent
  für `Version`, `AssemblyVersion`, `FileVersion` und `InformationalVersion` erhöhen.
- Nur einen sauberen, getesteten `main`-Stand taggen und veröffentlichen.
- `dist/ResticBrowser.exe` als exakt benanntes GitHub-Release-Asset hochladen,
  damit der stabile Link
  `releases/latest/download/ResticBrowser.exe` weiterhin funktioniert.
- SHA-256 vor dem Upload erfassen. Das veröffentlichte Asset anschließend erneut
  herunterladen und seinen Hash mit dem lokalen geprüften Build vergleichen.
- Releases als `latest`, nicht als Draft oder Prerelease veröffentlichen, sofern
  der Benutzer nichts anderes verlangt.
