# Entwicklungsstatus

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
