# AGENTS.md

## Dokumentationsgrenze

Zu Beginn jeder Sitzung `STATUS.md`, `PROJECT-STATE.md`, README und den
tatsächlichen Git-Status lesen. `STATUS.md` enthält den knappen aktuellen
Arbeitsstand, offene Aufgaben, Entscheidungen und nächste Schritte. Relevante
Änderungen dort nachführen, ohne vorhandene Informationen zu entfernen.
Projektkonventionen in dieser Datei, unter `docs/` und im Code beachten.
Öffentliche Dokumentation darf keine Zugangsdaten, privaten Notizen oder
vertraulichen Infrastrukturdetails enthalten.

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

- Vor der Arbeit `git status --short --branch` und den aktiven Branch prüfen;
  vorhandene Branches und fremde Arbeitsbaumänderungen respektieren.
- Änderungen gezielt stagen und committen; keine pauschalen Commits fremder Dateien.
- Vor einem Push passende Tests sowie den Secret-Scan für ausgehende Commits
  ausführen. Niemals ungefragt pushen oder Sicherheitsprüfungen mit
  `--no-verify`, geänderten Hook-Pfaden oder ähnlichen Mitteln umgehen.
- Den versionierten Pre-Push-Hook gemäß `docs/development-workflow.md`
  unter Windows und CachyOS installieren. Fehlende Prüfwerkzeuge sind ein
  Fehler und müssen behoben werden.

- Vor jeder Änderung einen eigenen Feature-Branch erstellen.
- Neue Aufgaben auf `codex/<thema>` von aktuellem `origin/main` beginnen. Bei
  Rechnerwechsel denselben Branch per Fetch und Fast-Forward-Pull fortsetzen;
  vorher den Arbeitsbaum prüfen und ungesicherte oder fremde Änderungen bewahren.
- Kein Force Push, kein Rebase veröffentlichter Commits und kein automatischer
  Merge. Nach einem PR-Merge `main` per Fast-Forward aktualisieren und für die
  nächste Aufgabe einen neuen Branch anlegen.
- Der geschützte Branch `main` darf ausschließlich über Pull Requests geändert werden; niemals direkt auf `main` committen oder pushen.
- Bei einem Konflikt-PR zuerst den aktuellen Stand von `origin/main` einlesen und in den
  Feature-Branch zusammenführen oder den Branch bewusst darauf rebasieren. Bereits vorhandene
  Funktionen aus `main` und unbeteiligte Arbeitsbaumänderungen dürfen dabei nicht verloren gehen.
- Vor dem Push `git status`, den Diff gegen `origin/main` und `git diff --check` prüfen. Nur den
  Feature-Branch pushen und nach Änderungen die PR-CI erneut abwarten.

## Versioning and Releases

Dieses Repository verwendet `version` unter `[workspace.package]` in `rust/Cargo.toml` als alleinige Quelle der Produktversion. Für jede
funktionale Änderung:

- PATCH, MINOR oder MAJOR nach Semantic Versioning bestimmen
- `rust/Cargo.toml` und die dazugehörige `rust/Cargo.lock` im selben Pull Request aktualisieren
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
`workflow_dispatch` laufen. Releases werden ausschließlich über den manuellen
Start von `release.yml` auf `main` mit Eingabe von `version` angefordert.
Eine höhere Version erstellt einen Versions-PR; nach dessen manuellem Merge und
erfolgreicher Main-CI wird der Release fortgesetzt. Entspricht die Eingabe bereits
der Produktversion auf `main`, durchläuft dieser Commit direkt die Release-Prüfungen.
Normale Versionsänderungen oder Merges dürfen keine Veröffentlichung auslösen.

## Projekt

Restic Browser ist eine deutschsprachige, portable Rust-Anwendung (egui) für Windows und Linux.
Sie durchsucht vorhandene Restic-Repositories und stellt ausgewählte Dateien oder
Ordner wieder her. Restic bleibt die einzige Schnittstelle zum Repository. Der Zugriff auf
Repository-Daten bleibt lesend. Restore darf ausschließlich den ausdrücklich gewählten
Zielpfad außerhalb des Repositorys verwenden. Mount gibt es seit 4.0.0 nicht mehr; eine
Wiedereinführung braucht eine ausdrücklich neue Projektentscheidung.

## Rust-Version (seit 4.0.0)

Die Anwendung ist vollständig in Rust geschrieben; der C#-Code wurde entfernt. Der Cargo-Workspace
liegt unter `rust/` (`restic-core`, `restic-app`, `restic-ui`, `restic-testkit`). Plan, Verlauf,
Grenzwerte und Testzuordnung der Migration stehen in `docs/rust-migration.md`, die Paritätsliste in
`docs/rust-parity.md`.

- Fachliche Regeln dieser Datei: nur lesend, Restic als einzige Schnittstelle, Start ohne Shell,
  Secrets nur im Speicher, deutsche Texte, keine Laufzeit-Downloads, keine dauerhaften Caches,
  Grenzwerte unverändert.
- `unsafe` ist im Workspace verboten (`unsafe_code = "forbid"`). Neue Abhängigkeiten müssen
  `cargo deny check licenses` und `cargo audit` bestehen; die Projektlizenz ist Apache 2.0.
- Fixtures unter `rust/fixtures/` nur über `rust/fixtures/generate.sh` erzeugen und vor dem
  Commit auf lokale Pfade, Benutzer- und Rechnernamen prüfen.
- Namenskonvention wegen Secret-Scan: Der Pre-Push-Hook meldet Wörter wie Passwort, Secret oder Token,
  gefolgt von `:` oder `=` und mindestens 12 Zeichen. Deshalb heißt der Abbruchparameter `stop`;
  Variablen und Felder mit solchen Namen vermeiden diese Form (zum Beispiel `pw`).
- Mount ist entfernt. Dauerhafte Prozess- oder Dateisystem-Einbindungen von Snapshots nicht ergänzen.

## Technische Leitlinien

- Zielplattform: Rust (Edition 2024, stabile Toolchain), egui/eframe 0.37 mit glow, `win-x64` und `linux-x64`.
- Keine Funktionen implementieren, die Repository-Daten initialisieren, löschen,
  bereinigen/prunen, reparieren oder anderweitig verändern. Dazu gehören insbesondere
  `init`, `forget`, `prune` und Reparaturprüfungen.
- Restic-Prozesse immer ohne Shell über `std::process::Command`/`tokio::process::Command` mit getrennten Argumenten starten.
- Passwörter und Backend-Geheimnisse ausschließlich in der Prozessumgebung und im
  Arbeitsspeicher halten. Niemals protokollieren oder in Einstellungen speichern.
- JSON-Ausgaben tolerant gegen zusätzliche Felder und unbekannte Nachrichtentypen
  verarbeiten.
- Alle sichtbaren Texte und verständlichen Fehlermeldungen bleiben auf Deutsch.
- Helles und dunkles Design müssen für alle egui-Widgets und Dialoge funktionieren.

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
- Es gibt keine Laufzeitdiagnose oder dauerhafte Telemetrie.

## Schutz des Repositorys

- Der normale Workflow bleibt strikt lesend: Snapshot-Liste, Suche, Vorschau,
  Statistik, Integritätsprüfung und Restore-Vorschau dürfen keine Snapshot- oder
  Repository-Daten verändern.
- Restore schreibt nur in ausdrücklich gewählte Zielpfade. Vorhandene
  Zieldateien, Abbruch und Fehler müssen gemäß der jeweiligen Restore-Regeln behandelt
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
- Gemeinsame Typografie und Widget-Varianten zentral in `rust/crates/restic-ui/src/theme.rs` definieren;
  Fenster und Dialoge verwenden insbesondere `title`, `section_title`, `secondary`, `small`,
  `primary_button`, `quiet_button` und `dialog_frame` konsistent.
- Farben ausschließlich über die `Palette` und `theme::apply` in `theme.rs` definieren.
  Keine fest eingebauten hellen Systemfarben verwenden.
- Neue oder geänderte Controls in Hell und Dunkel prüfen. Das umfasst Normal,
  Hover, Pressed, Fokus, Auswahl, Disabled, Ladezustand und geöffnete Popups.
- Standard-Stile von egui insbesondere für Tabellen, `ComboBox`, Listen, Eingabefelder
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

- `rust/crates/restic-ui/assets/app.ico` ist das Windows-EXE- und Fenster-Icon (Installer und `build.rs`).
- `rust/crates/restic-ui/assets/app-icon.png` ist die hochauflösende Darstellung im App-Header und Fenstersymbol.
- Beide Dateien gemeinsam aktualisieren; das eingebettete Windows-EXE-Icon und die EXE-Version nach dem Build kontrollieren.

## Gemeinsamer Arbeitsablauf

Windows und CachyOS mit VS Code und lokaler Codex CLI sind gleichberechtigte
Entwicklungsumgebungen. Pfade, Skripte und Tests plattformübergreifend halten;
Windows-spezifische Schritte ausdrücklich kennzeichnen. Lokale Konfiguration,
Tokens und Repository-Passwörter nie committen oder in Logs ausgeben.

Vor einer Aufgabe README, `PROJECT-STATE.md` und relevante `docs\` lesen.
Architektur respektieren, Änderungen klein halten und die passenden vorhandenen
Prüfungen ausführen. `PROJECT-STATE.md` nur aktualisieren, wenn sich der dokumentierte
Projektstand, eine Entscheidung oder ein offener Prüfpunkt tatsächlich ändert. README oder
`docs\` nur bei geändertem Verhalten, Bedienung oder einer dauerhaft relevanten Entscheidung
anpassen.

## Prüfen

```sh
cd rust
cargo fmt --check
cargo clippy --workspace --all-targets --locked -- -D warnings
RESTIC_BROWSER_REQUIRE_RESTIC=1 cargo test --workspace --release --locked
cargo deny check licenses
cargo audit
cd ..
python3 scripts/verify-version.py
python3 -m unittest discover -s tests/scripts -p "test_*.py"
git diff --check
# unter Linux, fasst die Rust-Prüfungen samt Xvfb-Start zusammen (mit --package zusätzlich das Paket):
sh rust/scripts/linux-verify.sh [--package]
```

```powershell
# unter Windows (benötigt GnuPG, Python 3; -Installer zusätzlich Inno Setup 6):
./rust/scripts/package-windows.ps1 [-Installer]
```

Die Tests enthalten lokale Restic- und Performance-Tests sowie einen End-to-End-Test, der ein temporäres
Repository erstellt und anschließend vollständig entfernt. Sie brauchen restic (ab 0.17.1) im `PATH` oder in
`RESTIC_BROWSER_TEST_RESTIC`; mit `RESTIC_BROWSER_REQUIRE_RESTIC=1` ist ein fehlendes restic ein Fehler.
Fehlende Plattformabhängigkeiten werden als übersprungen ausgewiesen. Die Paketierung kann lokal zusätzlich
PowerShell, GnuPG, bzip2 oder Inno Setup benötigen. Unter Linux läuft die Oberfläche in Prüfungen mit
`xvfb-run`; dort `WAYLAND_DISPLAY` entfernen und `XDG_SESSION_TYPE=x11` setzen.

## CI/CD und GitHub Actions

- `.github/workflows/build.yml`: Multi-Plattform-CI (`windows-latest` und `ubuntu-latest`) mit
  zentralen Composite Actions unter `.github/actions` für Rust-Setup, Restic-Installation sowie
  Build und Tests. Sie prüft Formatierung, Clippy, Lizenzen, bekannte Schwachstellen,
  Versionskonsistenz, Release-Skripte, Tests, den Xvfb-Start in beiden Themes, die Linux-Pakete
  auf Ubuntu und Arch und erzeugt PR-Preview-Artefakte.
- `.github/workflows/release.yml`: Wird manuell auf `main` mit Versionseingabe gestartet und
  nach einem Versions-PR durch erfolgreiche Main-CI fortgesetzt; baut und prüft
  Windows- und Linux-Artefakte, erstellt `SHA256SUMS.txt`, erstellt bzw.
  prüft den exakten Tag und veröffentlicht den GitHub Release idempotent.
- `.github/dependabot.yml`: Automatisierte Updates für Cargo-Abhängigkeiten (`/rust`) und GitHub Actions.

## Releases

- Vor jeder funktionalen Änderung die bestehende Version aus `rust/Cargo.toml`
  lesen und nach Semantic Versioning als PATCH, MINOR oder MAJOR einordnen.
- `version` unter `[workspace.package]` enthält ausschließlich `MAJOR.MINOR.PATCH`; die Änderung (samt
  `Cargo.lock`) gehört in denselben Pull Request wie die eigentliche Änderung.
- Alle Crates erben die Version; die Windows-EXE erhält Datei- und Produktversion daraus (`build.rs`).
  `scripts/verify-version.py` prüft die Konsistenz.
- **Ablauf für ein neues Release:**
  1. In Actions `Release starten` auf `main` öffnen und `version` eingeben.
  2. Bei einer höheren Version den erzeugten Versions-PR nach erfolgreicher CI mergen.
     Eine bereits auf `main` vorhandene Produktversion startet direkt die Release-Prüfungen.
  3. Nach einem Versions-PR wartet die automatische Fortsetzung auf erfolgreiche Main-CI.
  4. Nach erfolgreichen Release-Prüfungen erstellt GitHub Actions den annotierten Tag
     `vX.Y.Z` auf dem geprüften Commit und veröffentlicht den GitHub Release.
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

