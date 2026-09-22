# AGENTS.md

## Dokumentationsgrenze

Dieses Repository ist die maßgebliche Quelle für projektspezifischen Code,
README, Architektur, Entscheidungen und `PROJECT-STATE.md`. Vor größeren
Änderungen zuerst diese Dateien und relevante Dateien unter `docs\` lesen.
Der dort dokumentierte Status ist branchbezogen und darf nicht gegen den tatsächlich
ausgecheckten Branch, `git status` oder den aktuellen Code ausgespielt werden. Bei einer
Abweichung zuerst den realen Stand prüfen und die Abweichung ausdrücklich festhalten.

Homelab darf nur gezielt für allgemeines Restic-, Backup- und Restorewissen
durchsucht werden. Projektstatus, Projekt-TODOs und konkrete Konfigurationen
werden nicht im Homelab geführt.

## Git-Workflow

- Vor jeder Änderung einen eigenen Feature-Branch erstellen.
- Der geschützte Branch `main` darf ausschließlich über Pull Requests geändert werden; niemals direkt auf `main` committen oder pushen.
- Bei einem Konflikt-PR zuerst den aktuellen Stand von `origin/main` einlesen und in den
  Feature-Branch zusammenführen oder den Branch bewusst darauf rebasieren. Bereits vorhandene
  Funktionen aus `main` und unbeteiligte Arbeitsbaumänderungen dürfen dabei nicht verloren gehen.
- Vor dem Push `git status`, den Diff gegen `origin/main` und `git diff --check` prüfen. Nur den
  Feature-Branch pushen und nach Änderungen die PR-CI erneut abwarten.

## Versioning and Releases

Dieses Repository verwendet `version.txt` als alleinige Quelle der Produktversion. Für jede
funktionale Änderung:

- PATCH, MINOR oder MAJOR nach Semantic Versioning bestimmen
- `version.txt` im selben Pull Request aktualisieren
- die neue Version im Pull Request nennen

Reine Dokumentations- oder Agent-Regeländerungen ohne Produktverhalten benötigen keine
Versionsänderung. Bei funktionalen Änderungen gilt:

- PATCH für Fehlerbehebungen, kleine Korrekturen, Refactoring und interne technische
  Verbesserungen ohne neue sichtbare Fähigkeit
- MINOR für abwärtskompatible Funktionen oder Optionen
- MAJOR für inkompatible Änderungen und entfernte Funktionalität
- bei unklarem Einfluss standardmäßig PATCH

Git-Tags und GitHub-Releases niemals manuell erstellen, verschieben, löschen oder blind
überschreiben.

Die normale CI muss für Pull Requests nach `main`, Pushes auf `main` und
`workflow_dispatch` laufen. `release.yml` läuft nach einem Push auf `main`, der `version.txt`
ändert, oder nach einem ausdrücklich angeforderten manuellen Start auf `main`. Nach erfolgreicher
Prüfung erstellt dieser Workflow den passenden Tag `vX.Y.Z` und den GitHub Release.

## Projekt

Restic Browser ist eine deutschsprachige, portable Avalonia-Anwendung für Windows und Linux.
Sie durchsucht vorhandene Restic-Repositories und stellt ausgewählte Dateien oder
Ordner wieder her. Restic bleibt die einzige Schnittstelle zum Repository. Der Zugriff auf
Repository-Daten bleibt lesend. Restore, TAR-Export und Mount dürfen ausschließlich die jeweils
ausdrücklich gewählten Zielpfade außerhalb des Repositorys verwenden.

## Technische Leitlinien

- Zielplattform: `net10.0`, Avalonia 12.1.2, `win-x64` und `linux-x64`.
- Keine Funktionen implementieren, die Repository-Daten initialisieren, löschen,
  bereinigen/prunen, reparieren oder anderweitig verändern. Dazu gehören insbesondere
  `init`, `forget`, `prune` und Reparaturprüfungen.
- Restic-Prozesse immer ohne Shell über `ProcessStartInfo.ArgumentList` starten; das gilt auch
  für den Remote-Helfer.
- Passwörter und Backend-Geheimnisse ausschließlich in der Prozessumgebung und im
  Arbeitsspeicher halten. Niemals protokollieren oder in Einstellungen speichern.
- JSON-Ausgaben tolerant gegen zusätzliche Felder und unbekannte Nachrichtentypen
  verarbeiten.
- Alle sichtbaren Texte und verständlichen Fehlermeldungen bleiben auf Deutsch.
- Helles und dunkles Design müssen für Avalonia-Controls und Dialoge funktionieren.

## Restic-Auflösung und Performance

- Restic bleibt die einzige Repository-Schnittstelle. Es wird keine Restic-Go-Bibliothek
  direkt in die Anwendung eingebunden und es gibt keine Laufzeit-Downloads.
- Die feste Auflösungsreihenfolge lautet: ausdrücklich ausgewählte Datei, geprüfte
  mitgelieferte Version, portable Datei neben der Anwendung oder in `tools`, danach
  Systempfad beziehungsweise `PATH`; WinGet-Pfade gelten zusätzlich nur unter Windows.
- Die gebündelte Restic-Version ist 0.19.1. Das Build-Manifest pinnt Archive und Hashes;
  `scripts/prepare-restic.ps1` prüft die offizielle SHA-256-Prüfsumme und OpenPGP-Signatur.
  Zur Laufzeit gibt es keine Downloads.
- Die gebündelte Windows-Datei wird als Ressource eingebettet und erst bei Bedarf atomar,
  gesperrt und SHA-256-geprüft im Benutzerdatenordner bereitgestellt. Linux-Pakete enthalten
  `tools/restic`. Beschädigte oder parallele Provisionierungen dürfen keine ausführbare
  Zieldatei hinterlassen.
- Die tatsächlich gestartete Datei muss vor der Repository-Nutzung mit
  `restic version --json` validiert werden. Eine Herkunfts- oder Versionsanzeige darf nicht
  allein aus einer Konstanten abgeleitet werden.
- Beim Verbinden keinen automatischen `stats`-Aufruf starten; die Snapshot-Anzahl stammt aus
  `Snapshots.Count`. `GetStatsAsync` bleibt eine bewusst gestartete Funktion.
- JSON-Array- und `find`-Streams frühzeitig beenden, sobald das Ergebnis vollständig ist:
  `FindNewestAsync` nach dem ersten gültigen Treffer und `FindAsync` nach 10.000 Treffern.
  Der Runner muss dabei stdout/stderr sauber leeren, den Prozess beenden und den absichtlichen
  Abbruch nicht als Fehler melden. Ein normaler Benutzerabbruch bleibt ein Abbruch.
- Batch-Verarbeitung mit höchstens 256 Treffern, der begrenzte LRU-Verzeichnis-Cache und die
  Suchgrenze von 10.000 Treffern bleiben erhalten. Dauerhafte Ergebnis-Caches sind verboten.
- Der lokale Command-Monitor bleibt standardmäßig deaktiviert und darf ausschließlich
  Befehlstyp, Backend-Typ, Ausgabezeiten/-menge, Exit-Code und frühen Abbruch erfassen.
  Repository-Pfade, Argumentwerte, Passwörter und Umgebungsvariablen dürfen nie aufgezeichnet
  werden; es gibt keine dauerhafte Telemetrie.

## Schutz des Repositorys

- Der normale Workflow bleibt strikt lesend: Snapshot-Liste, Suche, Vorschau, Vergleich,
  Statistik, Integritätsprüfung, Restore-Vorschau und Mount dürfen keine Snapshot- oder
  Repository-Daten verändern.
- Restore und TAR-Export schreiben nur in ausdrücklich gewählte Zielpfade. Vorhandene
  Zieldateien, Abbruch und Fehler müssen gemäß der jeweiligen Restore-/Export-Regeln behandelt
  werden; ein Repository-Schreibzugriff darf daraus nicht entstehen.
- Eine Funktion zum Löschen, Bereinigen, Reparieren oder Erstellen von Snapshots und
  Repository-Daten darf nur nach einer ausdrücklich neuen Projektentscheidung eingeführt
  werden. Bis dahin bleiben solche Restic-Befehle ausgeschlossen.

## Oberfläche und Themes

- Das Hauptfenster bleibt klar in Verbindung, Repository-Werkzeuge, Snapshot-Auswahl
  und Dateibrowser gegliedert. Primäre Dateiaktionen stehen direkt am Dateibrowser;
  seltenere Repository-Werkzeuge bleiben in einer getrennten Werkzeugleiste.
- Snapshot-Filter bleiben einklappbar, damit die Snapshot-Liste im Normalzustand
  möglichst viel Platz erhält.
- Keine Lesezeichen-Funktion hinzufügen. Eine Snapshot-Löschaktion ist aktuell nicht
  vorhanden und darf ohne neue Projektentscheidung nicht ergänzt werden. Sollte sie später
  ausdrücklich beschlossen werden, gehört sie ausschließlich in einen klar getrennten,
  als gefährlich erkennbaren Repository-Werkzeugbereich und niemals neben die primäre
  Wiederherstellungsaktion. Zusätzliche dauerhafte Navigationselemente nur bei nachgewiesenem
  Bedarf einführen.
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
Prüfungen ausführen. `PROJECT-STATE.md` nur aktualisieren, wenn sich der dokumentierte
Projektstand, eine Entscheidung oder ein offener Prüfpunkt tatsächlich ändert. README oder
`docs\` nur bei geändertem Verhalten, Bedienung oder einer dauerhaft relevanten Entscheidung
anpassen.

## Prüfen

```powershell
dotnet format --verify-no-changes
dotnet list package --vulnerable --include-transitive
dotnet build ResticBrowser.slnx -c Release
dotnet run --project tests/ResticBrowser.Tests/ResticBrowser.Tests.csproj -c Release --no-build
./scripts/verify-version.ps1
git diff --check
./scripts/publish-windows.ps1
./scripts/publish-windows-installer.ps1
# unter Linux:
./scripts/publish-linux.sh
```

Der Test-Runner enthält lokale Restic- und Performance-Tests sowie einen End-to-End-Test, der
ein temporäres Repository erstellt und anschließend vollständig entfernt. Linux-/OpenSSH-
Tests benötigen die dafür vorgesehenen Umgebungsvariablen; fehlende Plattformabhängigkeiten
werden als übersprungen ausgewiesen. Die Paketierung kann lokal zusätzlich GnuPG, bzip2 oder
Inno Setup benötigen.

## CI/CD und GitHub Actions

- `.github/workflows/build.yml`: Multi-Plattform-CI (`windows-latest` und `ubuntu-latest`) mit
  zentralen Composite Actions unter `.github/actions` für .NET-Setup, Restic-Installation sowie
  Build und Tests. Sie prüft Formatierung, bekannte Paket-Schwachstellen, Versionskonsistenz,
  Build, Tests und PR-Preview-Artefakte.
- `.github/workflows/release.yml`: Läuft bei einer Versionsänderung auf `main` oder manuell auf
  `main`, baut und prüft Windows- und Linux-Artefakte, erstellt `SHA256SUMS.txt`, erstellt bzw.
  prüft den exakten Tag und veröffentlicht den GitHub Release idempotent.
- `.github/dependabot.yml`: Automatisierte Updates für NuGet-Pakete und GitHub Actions.

## Releases

- Vor jeder funktionalen Änderung die bestehende Version aus `version.txt`
  lesen und nach Semantic Versioning als PATCH, MINOR oder MAJOR einordnen.
- `version.txt` enthält ausschließlich `MAJOR.MINOR.PATCH`; die Änderung gehört
  in denselben Pull Request wie die eigentliche Änderung.
- Über `Directory.Build.props` werden `Version`, `AssemblyVersion`,
  `FileVersion` und `InformationalVersion` der Haupt-App und des Remote-Helfers
  konsistent aus `version.txt` erzeugt.
- **Ablauf für ein neues Release:**
  1. `version.txt` passend zur Änderung erhöhen.
  2. Änderungen per Pull Request nach `main` bringen und die CI abwarten.
  3. Nach dem Merge prüft `release.yml` Version und Produktionsartefakte.
  4. GitHub Actions erstellt nach erfolgreicher Prüfung den annotierten Tag
     `vX.Y.Z` auf dem geprüften Merge-Commit und veröffentlicht den GitHub
     Release mit automatisch erzeugten Release Notes.
- Codex setzt, pusht, verschiebt oder löscht keine Tags und erstellt, löscht
  oder überschreibt keine GitHub Releases außerhalb des vorgesehenen CI-Workflows.
- `ResticBrowser.exe` als exakt benanntes Windows-GitHub-Release-Asset veröffentlichen,
  damit der stabile Link `releases/latest/download/ResticBrowser.exe` weiterhin funktioniert.
- Zusätzlich `ResticBrowserWindows-<version>-win-x64.zip`, `ResticBrowser-Setup.exe` und
  `ResticBrowser-linux-x64.tar.gz` als Release-Assets veröffentlichen.
- SHA-256 vor dem Upload erfassen. Die veröffentlichten Assets anschließend erneut herunterladen
  und ihre Hashes mit den lokalen geprüften Artefakten vergleichen.
- Releases als `latest`, nicht als Draft oder Prerelease veröffentlichen, sofern
  der Benutzer nichts anderes verlangt.

