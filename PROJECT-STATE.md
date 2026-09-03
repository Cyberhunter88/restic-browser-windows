# Project State

## Aktuelle Aufgabe

Portable Restic-Anwendung mit lesendem Normalbetrieb und ausdrücklich geschützten
destruktiven Sonderaktionen weiterentwickeln.

## Aktueller Status

- Branch: `feature/draft-release-test-main`.
- Der Release-Workflow läuft für den End-to-End-Test bewusst Draft-only.
- Produktversion: `0.3.5` zentral in `version.txt`; Haupt-App, Remote-Helfer und
  Installer übernehmen diese Version.
- Der Git-Index wurde mit dem vorhandenen Arbeitsbaum abgeglichen. Die
  irreführenden staged Löschmarkierungen und gleichnamigen untracked Dateien
  sind auf neun nachvollziehbare Projektänderungen reduziert.
- Avalonia, .NET 10, Windows x64 und Linux x64.
- Portable Linux-x64-Ausgabe mit ausführbarer Datei `ResticBrowser` und Ubuntu-GUI-Startprüfung in GitHub Actions.
- Zentrale Produktversion in `version.txt` mit Release-Please-Draft-Workflow,
  vollständiger Artefaktprüfung und manuellem Reparatur-Workflow.
- Snapshot-Auswahl, Vorschau, Suche, Restore, Vergleich, Speicheranalyse,
  Linux-VPS-Restore und Linux-Mount sind dokumentiert.
- Automatische Löschung, Bereinigung und Prune bleiben ausgeschlossen.

## Zuletzt erledigt

- Projektregeln, README, Status, Architektur und Entscheidungen geprüft.
- Architektur und Entscheidungen nach `docs\` übernommen.
- Linux-Projektpfade, TAR.GZ-Paketierung und Release-Artefaktprüfung ergänzt.
- Release-Please, lokale Release-Prüfung und remote verifizierter Draft-Upload ergänzt.
- PR-CI und Main-CI für den versions.txt-Workflow erfolgreich ausgeführt.

## Offene Aufgaben

- Draft-only-Testmodus für den vollständigen Release-Upload vorbereitet.
- Vollständige Assets werden vor und nach dem Upload remote inklusive SHA-256
  verifiziert.

## Nächster Schritt

Draft-only-Test-PR prüfen und nach dem Merge einen v0.3.5-Draft durch Release
Please erzeugen lassen. Danach den automatischen Publish-Schritt wieder in den
normalen Release-Workflow zurückführen; bis dahin darf kein Release
veröffentlicht werden.
