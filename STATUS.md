# Entwicklungsstatus

## Rust-Migration, Phase 0 (6. November 2026)

- Branch `codex/rust-migration` von `origin/main` (`0bdab31`, Version 3.0.9).
- Entscheidungen: Workspace in `rust/`, UI-Framework Slint (Lizenz und UI-Spike vor Phase 5
  offen, Fallback egui), Regeln in `AGENTS.md` ergänzt. Plan: `docs/rust-migration.md`.
- Erledigt: Testzuordnung der 66 C#-Tests auf Phasen, verbindliche Grenzwerte,
  bereinigte restic-0.19.1-Fixtures über `rust/fixtures/generate.sh` (Leak-Suche sauber).
- Nicht erledigt: Restore-Fixture (Windows meldet Zeitstempelfehler für übergeordnete
  Ordner, nur mit `--include` auf Unterordner), Rust-Toolchain ist lokal nicht installiert
  (`cargo` fehlt) und wird für Phase 1 benötigt.
- Reine Dokumentation und Fixtures, Produktversion bleibt 3.0.9. Nicht committet.
- Phase 1 umgesetzt (Rust 1.99.1 war bereits installiert, `cargo-audit` nachinstalliert):
  Workspace `rust/` mit `restic-core` und `restic-testkit`, 53 Tests grün, `fmt`, `clippy -D warnings`
  und `cargo audit` sauber. Details und offene Punkte (Prozessbaum beenden): `docs/rust-migration.md`.
- Phase 2 umgesetzt: Locator, Versionsprüfung, Resolver und atomare Provisionierung (75 Tests grün,
  `fmt`, `clippy -D warnings`, `cargo audit` sauber). Offen: Linux-Test unter CachyOS ausführen,
  Einbettung der Windows-Datei in Phase 6. Der Phase-1-Commit enthielt `runner-fixture.rs` wegen der
  Wurzel-`.gitignore` nicht; mit Phase 2 nachgetragen.
- Phase 3 (ohne Mount) umgesetzt: Repository-Service, Batching, Pfadschutz, Speicheranalyse, `scripted-restic`
  und End-to-End-Test mit echtem Restic (131 Tests grün, `fmt`, `clippy -D warnings`, `cargo audit` sauber).
  Befund: Restore aus Snapshots mit absoluten Windows-Pfaden endet mit Exitcode 1 (Zeitstempel von `C:\Users`);
  gilt auch für C# (Details in `docs/rust-migration.md`). Offen: Mount/Askpass, Linux-Tests unter CachyOS/CI.
- Phase 4 umgesetzt: neues Crate `restic-app` (Einstellungen im 3.x-Format, Filter, LRU-Cache, Navigation,
  `BrowserSession` mit Operationsversionierung und Streaming; 190 Tests im Workspace grün, `fmt`,
  `clippy -D warnings`, `cargo audit` sauber; Sitzungstests 20-mal ohne Fehlschlag).
- UI-Spike für Slint 1.18.1 fertig (`rust/spikes/slint-ui`, Ergebnisse in `docs/rust-ui-spike.md`): Hell/Dunkel
  zur Laufzeit, dunkle Titelleiste für Haupt- und Dialogfenster (per DWM bestätigt), Zustände, Popups,
  100.000 Zeilen (Alles auswählen 48 ms, Sortieren 65 ms, 128 MB). `StandardTableView` reicht nicht
  (keine Mehrfachauswahl, sichtbarer Anfasser), Tooltips und echte Modalität müssen selbst gebaut werden.
  Nicht geprüft: Linux, Datei-Dialog, echte Bedienung. Lizenz: Projekt ist MPL-2.0, Slint ist GPLv3 /
  Royalty-free (mit sichtbarem Hinweis) / kommerziell; Entscheidung und Rückfrage bei Slint stehen aus.
- Projektlizenz soll Apache 2.0 sein (eigener Branch `codex/lizenz-apache-2`, Commit `e89833c`, nicht gepusht).
  Dadurch scheidet Slint aus: `cargo deny check licenses` (neue `rust/deny.toml`) lässt alle 12 Slint-Pakete
  durchfallen. Zweiter Spike mit egui/eframe 0.37 (`rust/spikes/egui-ui`) besteht die Lizenzprüfung (nur
  Schriftlizenzen als dokumentierte Ausnahme) und erfüllt Hell/Dunkel, dunkle Titelleiste für Haupt- und
  Dialogfenster, Modal, zweites Fenster, Tooltips (eingebaut); 100.000 Zeilen: 1,6 ms UI-Aufbau je Bild,
  96 MB, 6,7 MB Datei. Mängel: Datei-Symbole und Sortierpfeile fehlen (Symbolschrift einbetten),
  Spaltenlinien dauerhaft sichtbar. Vergleich und Empfehlung (egui): `docs/rust-ui-spike.md`.
- Rust-Lizenzfelder (`license = "Apache-2.0"`) in allen Cargo-Dateien gesetzt.
- egui als UI-Framework bestätigt; der Lizenz-PR (`codex/lizenz-apache-2`) wird bewusst später gemeinsam gemacht.
- Phase 5, Teil 1 umgesetzt (`rust/crates/restic-ui`, Binär `restic-browser`): Hauptfenster, Verbinden- und
  Wiederherstellen-Dialog, Theme, Vektorsymbole, Mehrfachauswahl-Tabelle. Mit echtem restic geprüft (Verbinden,
  Navigation, Suche, Auswahl, Wiederherstellung von 8 Dateien, Hell/Dunkel, Titelleiste). Dabei zwei Fehler
  gefunden und behoben (Index-Absturz beim Ordnerwechsel, leere Namen bei Suchtreffern). Details und offene
  Fenster (Vorschau, Versionen, Prüfung, Statistik, Analyse, Mount): `docs/rust-migration.md`.
- Phase 5, Teil 2 umgesetzt: Dateivorschau (Text, Bild, Binärerkennung) und Dateiversionen mit Wiederherstellung
  einer Version. Mit echtem restic geprüft (Textvorschau, PNG, Versionsliste, Restore der älteren Fassung 16 statt
  32 Byte, Hell und Dunkel); 19 UI-Tests. Ein scheinbarer Restore-Fehler war eine falsche Test-Hilfe (siehe
  `docs/rust-migration.md`). Lizenzprüfung (`cargo deny`) mit den neuen Bild-Abhängigkeiten bestanden.
- Nächster Schritt: Integritätsprüfung, Statistik und Speicheranalyse; danach Fensterzustand, Barrierefreiheit,
  Linux.

## UX-Optimierungen 3.0.9 (4. November 2026)

- Branch `codex/ux-optimierungen` von `origin/main`; Produktversion auf Benutzerwunsch
  3.0.9 (laut Regeln wären neue Funktionen eigentlich MINOR).
- Hauptfenster: Leerzustand mit gespeicherten Profilen, Pfadleiste, Suchbereich
  (Dieser Snapshot / Neueste Version) mit Zurücksetzen, verschiebbarer Trenner,
  sortierbare Spalten (Ordner zuerst), „Berechtigungen“ optional, Auswahlzusammenfassung,
  relative Zeit in der Snapshot-Liste, kontextabhängiger Repository-Button,
  Ladebalken im Dateibereich, Tastaturkürzel (Strg+O/F/R, F5, Alt+←/→/↑, Enter, Esc).
- Verbindungsdialog: letztes Profil vorgewählt, Fokus im Passwortfeld, Enter verbindet,
  Restic/Backend-Variablen unter „Erweitert“, Fehler direkt im Dialog.
- Wiederherstellen: letztes Ziel gemerkt (nur Pfad), Vorschau nach Zielwahl,
  Erklärung zur Überschreiben-Option. Fensterzustand wird gespeichert.
- Restic-Fehler erhalten Handlungshinweise (`ErrorGuidance`).
- Bewusst zurückgestellt: Drag & Drop in den Explorer (braucht eigene Projektentscheidung).
- Lokal bestanden: Release-Build ohne Warnungen, Formatprüfung, Versionsprüfung
  (`powershell.exe`), NuGet-Schwachstellenprüfung, 60 Tests, UI-Layoutprüfung Hell/Dunkel.
  Nicht lokal prüfbar: zwei Tests brauchen `pwsh`, der E2E-Test braucht eine
  ausführbare Restic-Datei in `RESTIC_BROWSER_TEST_RESTIC` (WinGet-Link wurde nicht akzeptiert).
- Offen: Secret-Scan, Push und PR nach Freigabe; manuelle Prüfung mit echtem Repository.

## Optimierungen 3.0.8 (3. November 2026)

- Branch `codex/optimierungen-3-0-8`, Arbeitsbaum mit ungesicherten Änderungen
  fortgesetzt (noch nicht committet).
- Produktversion 3.0.8 (PATCH): Dateiversionen nutzen die bereits geladene
  Snapshot-Liste (`knownSnapshots`) statt einen weiteren `snapshots`-Aufruf zu
  starten. Beim Laden entfernte, nicht mehr unterstützte Profile werden einmalig
  in `settings.json.bak` gesichert und im Status gemeldet.
- Lokal bestanden: Release-Build ohne Warnungen/Fehler, 62/62 Anwendungstests,
  Formatprüfung, `git diff --check`. Hinweis: SDK 10.0.403 liegt unter
  `~/.dotnet`; dafür `DOTNET_ROOT` und `PATH` setzen.
- Offen: `verify-version.ps1`, Secret-Scan, Commit, Push und PR nach Freigabe.

## Projekt- und Sicherheitsprüfung (30. Oktober 2026)

- Ausgangsstand: sauberer Branch `main`, nach Fetch identisch mit `origin/main`;
  Feature-Branch `codex/project-security-review` erstellt. Ältere Branchangaben
  in dieser Chronik bleiben historische Einträge.
- Produktversion 3.0.7: Restore- und Mount-Ziele werden gegen Repository-Pfade
  einschließlich symbolischer Verknüpfungen geprüft; Restore-Auswahl behandelt
  Glob-Sonderzeichen als Dateinamen. Weitere entfernte Backend-Präfixe werden
  abgewiesen. .NET SDK 10.0.403 und zugehörige Lockdatei enthalten das
  Sicherheitsupdate auf Runtime 10.0.14.
- Release-Vorabprüfung akzeptiert fehlende Assets bei einer Wiederaufnahme und
  prüft vorhandene Dateien weiterhin auf Größe und Hash.
- Lokal bestanden: Release-Build, gesperrter Restore, NuGet-Schwachstellen- und
  Aktualisierungsprüfung, Format- und Versionsprüfung, 60 Anwendungstests und
  23 Python-Tests. Der Restic-Integrationstest prüft die Auswahl der Zeichen
  `*`, `?` und `[` gegen ähnlich benannte Dateien.
- Der Sicherheitsbericht zur Ausgangsrevision nennt die Restore-Muster als
  bestätigten Befund; die Abdeckung bleibt teilweise, da der Arbeitsbaum während
  der Prüfung geändert wurde und Binärdaten nicht vollständig geprüft wurden.
- Grenzen: Dateisystem-Aliase wie Bind-Mounts und gleichzeitige Änderungen an
  Pfaden sind durch den Pfadvergleich nicht vollständig erkennbar. Native
  Windows-CI und Paketierung sind lokal nicht bestätigt.

## PR-Vorlage entfernen (22. Oktober 2026)

- Neue Dokumentationsaufgabe auf `codex/remove-pr-template`, von aktuellem
  `origin/main` (`2c81f3e`) erstellt. Der zuvor verwendete PR-Branch
  `codex/reliability-optimizations` bleibt unverändert erhalten.
- Abweichung zur bisherigen Chronik: Diese beschreibt noch einen älteren
  Mehrrechner-Branch; der tatsächliche Branch wurde vor der Arbeit geprüft.
- `.github/pull_request_template.md` entfernt, damit neue Pull Requests nach
  Integration dieser Änderung keine vorgegebenen Abschnitte mehr erhalten.
- Zum Zeitpunkt der Bereinigung war PR #102 der einzige offene PR. Dessen
  Beschreibung wurde über die
  GitHub-CLI von leeren Vorlagenabschnitten und dem Versionsplatzhalter bereinigt;
  die vorhandene fachliche Beschreibung bleibt erhalten. Der GitHub-Connector
  meldete zuvor fehlende Schreibrechte (403).
- Reine Dokumentationsänderung ohne Versionsanhebung. `git diff --check`
  bestanden; keine weiteren Repository-Vorlagen gefunden.
- Der Benutzer hat die Erstellung eines eigenen PRs freigegeben. Der vorhandene
  Commit `2744a7f` bleibt erhalten; `origin/main` (`dd0c36a`, nach Merge von PR #102)
  wurde integriert. Beide Dokumentationsstände bleiben erhalten.
- Der Diff gegen `origin/main` enthält ausschließlich die Vorlagenlöschung und
  zugehörige Statusdokumentation; Anwendungscode und Produktversion bleiben gleich.

## PR #102: Prozess-Runner-CI reparieren (22. Oktober 2026)

- Tatsächlicher Ausgangsstand: sauberer Branch `codex/reliability-optimizations`,
  Commit `a283ae2`, identisch mit dem veröffentlichten PR-Head. Die unten erhaltene
  Notiz „uncommitted/kein PR“ beschreibt den früheren lokalen Stand.
- CI-Run `39733145950`: Preflight erfolgreich; Windows und Ubuntu scheitern im
  Runner-Test. Ubuntu meldet einen JSON-Fehler bei leerem stdout eines regulär
  fehlgeschlagenen Prozesses; Windows eine unspezifische Assertion.
- stdout-EOF und Prozessende werden jetzt getrennt behandelt. Nach tatsächlichem
  EOF wartet der JSON-Pfad auf den Exitcode; Abbruch und stderr-Cleanup bleiben
  erhalten. Ungültige Daten bei offenem Stream führen weiterhin zum Prozessende.
- Testprozesse schreiben ausdrücklich UTF-8. Fehler nennen den jeweiligen
  Runner-Testfall. Ein gesteuerter Python-Helfer schließt stdout vor Prozessende,
  ohne zusätzliche .NET-Host-Handles offen zu halten.
- Der neue Regressionstest schlägt gegen den ursprünglichen PR-Code fehl und
  besteht mit dem Fix in zehn aufeinanderfolgenden Durchläufen.
- Lokal bestanden: Release-Build ohne Warnungen/Fehler, 59/59 Anwendungstests
  ohne übersprungene Tests, Formatprüfung, Versionsprüfung und `git diff --check`.
  Secret-Musterscan des aktuellen Arbeitsbaums ohne Treffer; noch keine Prüfung
  eines neuen ausgehenden Reparatur-Commits.
- Windows-Bestätigung und erneute PR-CI stehen nach Freigabe des Pushs aus.
- Zielversion bleibt 3.0.6: Reparatur innerhalb desselben noch offenen PRs;
  dessen Versionsanhebung gegenüber `main` bleibt bestehen.
- Vor Freigabe: noch kein Reparatur-Commit oder Push; Remote-CI für den Fix
  nicht gestartet. Der Benutzer hat Commit und Push anschließend ausdrücklich
  freigegeben; die erneute PR-CI muss den veröffentlichten Fix bestätigen.

## Aktueller Arbeitsstand 3.0.6 (22. Oktober 2026)

- Umsetzung auf `codex/reliability-optimizations`, aus dem frisch abgeglichenen
  `origin/main` erstellt. Ausgangscommit: `2c81f3e`; Arbeitsbaum war sauber.
- Abweichung zur unten erhaltenen Chronik: Zu Sitzungsbeginn war `main`
  ausgecheckt, nicht der dort beschriebene Mehrrechner-Feature-Branch.
- Produktversion 3.0.6: Profildateien werden bei Ladefehlern nicht überschrieben;
  Speicherzugriffe verwenden eine Instanzsperre, eine Prozesssperre und eindeutige
  temporäre Dateien mit atomarem Austausch.
- Der Restic-Runner beendet Prozesse auch bei Parser-/Callback-Fehlern und leert
  stderr. Benutzerabbruch und frühes Suchende bleiben getrennt.
- Integritätsprüfung streamt Meldungen; sichtbare Details sind auf 64 KiB UTF-8
  begrenzt. Ergebnisfelder werden auch nach Erreichen dieser Grenze ausgewertet;
  der Kürzungshinweis steht vor den Details.
- .NET-SDK 10.0.402 mit `latestPatch`, NuGet-Lockdateien und gesperrter CI-Restore
  sind eingerichtet. Direkte Paketversionen bleiben unverändert.
- Der versionierte Pre-Push-Hook ist in diesem Klon aktiviert.
- Bestanden: Release-Build ohne Warnungen/Fehler, 58/58 Anwendungstests ohne
  übersprungene Tests, 23/23 Python-Tests, Format- und Versionsprüfung,
  NuGet-Schwachstellenprüfung ohne Treffer, gesperrter Restore und Negativtest
  gegen eine Paketänderung ohne aktualisierte Lockdatei.
- Hauptfenster (760/1099/1100/1360 DIP), Verbindungsdialog, Profilfehler und
  gekürzte Prüfdetails in Hell/Dunkel unter Xvfb erfasst; Hover/Fokus geprüft.
- Windows-EXE cross-kompiliert; PE-Ressourcen bestätigen 3.0.6.0/3.0.6.
  Die optionale EXE-Prüfung mit FileVersionInfo liefert unter Linux leere Werte;
  die zentrale Versionsprüfung ohne EXE-Parameter besteht.
- Linux-Archiv gebaut und mit dem vorhandenen Paketprüfer auf Restic-Version,
  Ausführungsrechte und GUI-Start aus einem Pfad mit Leerzeichen geprüft.
- Hook-Selbsttest und Secret-Musterscan des Arbeitsbaums ohne Treffer bestanden;
  dies ist keine Prüfung ausgehender oder veröffentlichter Commit-Historie.
- Offen: native Windows-CI und Windows-Installer (Inno Setup fehlt), Arch-Container
  (Docker fehlt), echter FUSE-Mount und Windows-Themeprüfung. Die Restic-Paketierung
  meldet bestehende GnuPG-Warnungen zu temporären Ordnerrechten und Schlüsselvertrauen;
  die Signaturprüfung mit dem festgelegten Fingerprint war erfolgreich.
- Änderungen bleiben lokal und uncommitted; kein Push, PR, Merge oder Release.
  Remote-CI wurde für diese Änderungen nicht gestartet.

## Erhaltene bisherige Statusnotizen

Stand: 18. Oktober 2026; Branchstatus und Code bei jeder Sitzung erneut prüfen.

## Aktueller Stand

- Am 19. Oktober 2026 wurde für den Mehrrechner-Workflow der lokale Branch
  `codex/multi-device-development` von sauberem `main` erstellt. Die Anleitung
  für Einrichtung, Rechnerwechsel und Arbeit nach einem Merge wird ergänzt;
  Commit und Push stehen unter ausdrücklichem Freigabevorbehalt.
- GitHub meldet `main` als geschützt und `build-and-test` als erforderlichen
  Check. Die Detailansicht der Branch Protection war für die verfügbare
  Integration nicht lesbar; die genauen Regeln müssen in GitHub geprüft werden.
- Der lokale Pre-Push-Hook ist über `core.hooksPath=.githooks` aktiviert;
  seine Shell-Syntax und `git diff --check` wurden geprüft. Formatierung,
  Versionsskript und Python-Workflowtests waren in dieser Ausführungsumgebung
  nicht abschließbar (Pipe-Zugriff, fehlendes `pwsh` beziehungsweise Python).
- Direkte NuGet-Versionen sind festgelegt, eine NuGet-Lockdatei fehlt. Node-
  und Go-Abhängigkeiten gibt es im aktuellen Projekt nicht.
- Produktversion laut `Directory.Build.props`: 3.0.4.
- Windows x64 und Linux x64 werden über die bestehende CI geprüft.
- Der ausführliche bisherige Projektverlauf bleibt in `PROJECT-STATE.md` erhalten.
- Dieser Branch richtet einen gemeinsamen Entwicklungsworkflow und eine lokale
  Push-Prüfung ein. Eine PR und deren CI sind noch offen.
- Der Musterscan fand im aktuellen Commitbaum keine Treffer. In der
  veröffentlichten Historie wurden frühere Test-/CI-Werte in den Commits
  `4b3cc86` und `f6e0735` zur manuellen Einordnung gefunden; keine Historie
  wurde verändert. Der gemeinsame Scanner war lokal nicht verfügbar.

## Offene Aufgaben

- Den Pre-Push-Hook auf Windows und CachyOS installieren und prüfen.
- Historische Test-/CI-Treffer mit dem gemeinsamen Scanner erneut prüfen,
  sobald dieser verfügbar ist.
- PR-CI nach einem ausdrücklich beauftragten Push abwarten.
- Die in `PROJECT-STATE.md` genannten plattformspezifischen offenen Prüfungen
  bei passenden Änderungen erneut bewerten.

## Technische Entscheidungen

- `PROJECT-STATE.md` bleibt die ausführliche branchbezogene Chronik;
  `STATUS.md` ist der kurze Einstieg für die nächste Sitzung.
- Der Hook prüft ausgehende Commits lokal; die bestehende CI prüft Build,
  Tests und Versionskonsistenz.
- Die Produktversion bleibt ausschließlich in `Directory.Build.props`.

## Nächste Schritte

1. Lokale Prüfungen und Hook-Selbsttest ausführen.
2. Änderungen gezielt committen; Push nur nach ausdrücklichem Auftrag.

## Prüfung dieses Branches

- Hook-Shell-Syntax und `git diff --check` bestanden.
- Release-Build und darauf aufbauende Tests konnten hier nicht laufen:
  NuGet-Restore scheiterte wegen nicht erreichbarem Paketindex (`NU1301`).
