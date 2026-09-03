# Project State

## Aktuelle Aufgabe

Portable Restic-Anwendung mit lesendem Normalbetrieb und ausdrücklich geschützten
destruktiven Sonderaktionen weiterentwickeln.

## Aktueller Status

- Branch: `feature/legacy-release-version-source`.
- Der bestehende PR-Branch `feature/robust-release-workflow` wird mit dieser
  Feature-Branch-Version aktualisiert.
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

## Offene Aufgaben

- Alten Build-und-Release-Aufbau mit zentraler `version.txt`, Release-Please-Draft
  und getrennten Windows-/Linux-Build-Jobs zusammengeführt.
- Vollständige Assets werden vor und nach der Veröffentlichung remote verifiziert.

## Nächster Schritt

Den aktualisierten PR in GitHub Actions prüfen. Lokal sind Formatierung,
Paket-Sicherheitsprüfung, Versionsprüfung, Release-Build und 38/38 Tests
auszuführen; der Linux-GUI-Starttest bleibt ein Ubuntu-CI-Gate. Ein echter
Release darf während der PR-Prüfung nicht veröffentlicht werden.
