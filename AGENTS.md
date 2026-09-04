# AGENTS.md

## Dokumentationsgrenze

Dieses Repository ist die maßgebliche Quelle für projektspezifischen Code,
README, Architektur, Entscheidungen und `PROJECT-STATE.md`. Vor größeren
Änderungen zuerst diese Dateien und relevante Dateien unter `docs\` lesen.

Homelab darf nur gezielt für allgemeines Restic-, Backup- und Restorewissen
durchsucht werden. Projektstatus, Projekt-TODOs und konkrete Konfigurationen
werden nicht im Homelab geführt.

## Git-Workflow

- Vor jeder Änderung einen eigenen Feature-Branch erstellen.
- Der geschützte Branch `main` darf ausschließlich über Pull Requests geändert werden; niemals direkt auf `main` committen oder pushen.

## Versioning and Releases

This repository uses `version.txt` as the source of truth for the application
version. For every functional change, determine whether the change is PATCH,
MINOR, or MAJOR and update `version.txt` in the same pull request. Never create
Git tags or GitHub releases manually. After the pull request is merged, the
GitHub Actions release workflow creates the corresponding `vX.Y.Z` tag and
GitHub Release.
CI must run for pull requests targeting the default branch and after merges or
pushes to the default branch. The release workflow must only run when
`version.txt` changes.

## Projekt

Restic Browser ist eine deutschsprachige, portable Avalonia-Anwendung für Windows und Linux.
Sie durchsucht vorhandene Restic-Repositories und stellt ausgewählte Dateien oder
Ordner wieder her. Restic bleibt die einzige Schnittstelle zum Repository. Der normale
Workflow bleibt lesend; eine ausdrücklich angeforderte, einzelne Snapshot-Löschung ist
als einzige zulässige Repository-Schreibaktion erlaubt, wenn die Regeln unten vollständig
eingehalten werden.

## Technische Leitlinien

- Zielplattform: `net10.0`, Avalonia 12.1, `win-x64` und `linux-x64`.
- Keine Funktionen implementieren, die Repository-Daten neu anlegen, reparieren,
  bereinigen/prunen oder außerhalb der ausdrücklich bestätigten Snapshot-Löschung
  verändern.
- Restic-Prozesse immer ohne Shell über `ProcessStartInfo.ArgumentList` starten.
- Passwörter und Backend-Geheimnisse ausschließlich in der Prozessumgebung und im
  Arbeitsspeicher halten. Niemals protokollieren oder in Einstellungen speichern.
- JSON-Ausgaben tolerant gegen zusätzliche Felder und unbekannte Nachrichtentypen
  verarbeiten.
- Alle sichtbaren Texte und verständlichen Fehlermeldungen bleiben auf Deutsch.
- Helles und dunkles Design müssen für Avalonia-Controls und Dialoge funktionieren.
- Die portable Restic-Suche neben der App, im Unterordner `tools` und über `PATH`
  beibehalten; WinGet-Pfade gelten zusätzlich nur unter Windows.

## Snapshot-Löschung

- Snapshot-Löschung ist ausschließlich nach ausdrücklicher Benutzeraktion zulässig.
  Automatische Löschung, Sammellöschung nach Host/Tag/Zeitraum, Löschen aller
  Snapshots sowie anschließendes `prune` bleiben verboten.
- Zunächst darf nur genau ein in der Oberfläche ausgewählter Snapshot gelöscht werden.
  Der Dialog muss Snapshot-ID, Zeitpunkt, Host und Pfad anzeigen und ausdrücklich auf
  die fehlende Wiederherstellbarkeit hinweisen.
- Die Bestätigung muss zweistufig sein: Das Repository-Passwort muss für diesen
  Vorgang erneut eingegeben werden und zusätzlich muss die vollständige Snapshot-ID
  als Bestätigung eingegeben werden. Ein einfaches Ja, eine Checkbox oder die bereits
  geöffnete Sitzung genügt nicht.
- Das erneut eingegebene Passwort darf nur im Arbeitsspeicher und in der
  Prozessumgebung des einzelnen Restic-Prozesses verwendet werden. Es darf nie
  gespeichert, geloggt, in Fehlermeldungen angezeigt oder an andere Prozesse
  weitergegeben werden.
- Der Löschbefehl muss ausschließlich die bestätigte Snapshot-ID adressieren und
  über `ProcessStartInfo.ArgumentList` gestartet werden. Keine Shell, keine freien
  Kommandozeilen und keine impliziten Filter verwenden.
- Nach erfolgreicher Löschung muss die Snapshot-Liste neu geladen und das Ergebnis
  sichtbar gemeldet werden. Bei Abbruch, falschem Passwort oder Fehlern darf die
  Oberfläche keinen Erfolg anzeigen.
- Löschlogik muss mit einem Fake-Runner sowie einem temporären Restic-Repository
  getestet werden. Tests müssen insbesondere falsche Passwort-/ID-Bestätigungen,
  Argumenttrennung und das Ausbleiben von `prune` abdecken.

## Oberfläche und Themes

- Das Hauptfenster bleibt klar in Verbindung, Repository-Werkzeuge, Snapshot-Auswahl
  und Dateibrowser gegliedert. Primäre Dateiaktionen stehen direkt am Dateibrowser;
  seltenere Repository-Werkzeuge bleiben in einer getrennten Werkzeugleiste.
- Snapshot-Filter bleiben einklappbar, damit die Snapshot-Liste im Normalzustand
  möglichst viel Platz erhält.
- Keine Lesezeichen-Funktion hinzufügen. Eine Snapshot-Löschaktion gehört ausschließlich
  in einen klar getrennten, als gefährlich erkennbaren Repository-Werkzeugbereich und
  niemals neben die primäre Wiederherstellungsaktion. Zusätzliche dauerhafte
  Navigationselemente nur bei nachgewiesenem Bedarf einführen.
- Aktionsbeschriftungen kurz, eindeutig und ohne rein dekorative Emojis formulieren.
  Datei- und Inhaltstyp-Symbole dürfen zur schnellen visuellen Unterscheidung dienen.
- Gemeinsame Typografie und Control-Varianten über Styles in `App.axaml` definieren;
  Fenster und Dialoge verwenden insbesondere `title`, `sectionTitle`, `secondary`,
  `small`, `panel`, `toolbar`, `primary` und `quiet` konsistent.
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

## Gemeinsamer Arbeitsablauf

Vor einer Aufgabe README, `PROJECT-STATE.md` und relevante `docs\` lesen.
Architektur respektieren, Änderungen klein halten und die passenden vorhandenen
Prüfungen ausführen. Danach Status aktualisieren; README oder `docs\` nur bei
geändertem Verhalten oder geänderter Bedienung anpassen.

## Prüfen

```powershell
dotnet format --verify-no-changes
dotnet list package --vulnerable --include-transitive
dotnet build ResticBrowser.slnx -c Release
dotnet run --project tests/ResticBrowser.Tests/ResticBrowser.Tests.csproj -c Release --no-build
./scripts/publish-windows.ps1
./scripts/publish-windows-installer.ps1
# unter Linux:
./scripts/publish-linux.sh
```

Der Test-Runner enthält einen lokalen End-to-End-Test, der ein temporäres
Restic-Repository erstellt und anschließend vollständig entfernt.

## CI/CD und GitHub Actions

- `.github/workflows/build.yml`: Multi-Plattform-CI für Pull Requests und Pushes auf `main` (`windows-latest` und `ubuntu-latest`). Prüft C#-Formatierung (`dotnet format`), bekannte Paket-Schwachstellen (`dotnet list package --vulnerable`), baut die Lösung, führt Integrationstests aus und stellt Preview-Artefakte für Pull Requests bereit.
- `.github/workflows/release.yml`: Automatischer Release-Workflow für Windows & Linux. Läuft nach einer Änderung an `version.txt` auf `main`, baut und testet das Repository, erstellt alle Release-Artefakte, setzt den passenden Tag und veröffentlicht das GitHub Release.
- `.github/dependabot.yml`: Automatisierte wöchentliche Updates für NuGet-Pakete und GitHub Actions.

## Releases

- Vor jedem Release die semantische Version in `version.txt` erhöhen. Über
  `Directory.Build.props` müssen `Version`, `AssemblyVersion`, `FileVersion` und
  `InformationalVersion` der Haupt-App und des Remote-Helfers daraus konsistent
  erzeugt werden.
- Nur einen sauberen, getesteten `main`-Stand taggen und veröffentlichen.
- **Ablauf für ein neues Release:**
  1. Die semantische Version ausschließlich in `version.txt` erhöhen und die
     Änderung per Pull Request in `main` mergen.
  2. GitHub Actions (`release.yml`) startet automatisch, weil `version.txt`
     geändert wurde.
  3. Der Workflow prüft, baut und testet Windows und Linux. Erst danach erstellt
     er den annotierten Tag `vX.Y.Z` auf dem geprüften Merge-Commit.
  4. Anschließend wird der GitHub Release mit den vollständigen Artefakten
     veröffentlicht.
- Für einen einmaligen Bootstrap oder eine bewusst gestartete Wiederholung darf
  `release.yml` manuell auf `main` gestartet werden. Starts von Feature-Branches
  werden abgelehnt; der reguläre Ablauf bleibt die Änderung von `version.txt`.
- `dist/ResticBrowser.exe` als exakt benanntes Windows-GitHub-Release-Asset hochladen,
  damit der stabile Link `releases/latest/download/ResticBrowser.exe` weiterhin funktioniert.
- Zusätzlich `dist/ResticBrowserWindows-<version>-win-x64.zip`, `dist/ResticBrowser-Setup.exe` und `dist/ResticBrowser-linux-x64.tar.gz` als Release-Assets hochladen.
- SHA-256 vor dem Upload erfassen. Das veröffentlichte Asset anschließend erneut
  herunterladen und seinen Hash mit dem lokalen geprüften Build vergleichen.
- Releases als `latest`, nicht als Draft oder Prerelease veröffentlichen, sofern
  der Benutzer nichts anderes verlangt.

