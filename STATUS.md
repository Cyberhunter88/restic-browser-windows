# Entwicklungsstatus

## PR-Vorlage entfernen (22. Oktober 2026)

- Neue Dokumentationsaufgabe auf `codex/remove-pr-template`, von aktuellem
  `origin/main` (`2c81f3e`) erstellt. Der zuvor verwendete PR-Branch
  `codex/reliability-optimizations` bleibt unverändert erhalten.
- Abweichung zur bisherigen Chronik: Diese beschreibt noch einen älteren
  Mehrrechner-Branch; der tatsächliche Branch wurde vor der Arbeit geprüft.
- `.github/pull_request_template.md` entfernt, damit neue Pull Requests nach
  Integration dieser Änderung keine vorgegebenen Abschnitte mehr erhalten.
- PR #102 ist der einzige offene PR. Dessen Beschreibung wurde über die
  GitHub-CLI von leeren Vorlagenabschnitten und dem Versionsplatzhalter bereinigt;
  die vorhandene fachliche Beschreibung bleibt erhalten. Der GitHub-Connector
  meldete zuvor fehlende Schreibrechte (403).
- Reine Dokumentationsänderung ohne Versionsanhebung. `git diff --check`
  bestanden; keine weiteren Repository-Vorlagen gefunden. Die Dateilöschung
  bleibt lokal, bis Commit und Push für diesen neuen Branch freigegeben sind.

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
