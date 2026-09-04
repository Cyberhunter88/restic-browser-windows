# Project State

## Aktuelle Aufgabe

Direkte, getestete GitHub-Releases über die zentrale Versionsquelle
`version.txt` bereitstellen.

## Aktueller Status

- Branch: `feature/direct-version-release-cleanup`.
- Produktversion: `0.3.6` in `version.txt`.
- Haupt-App und Remote-Helfer beziehen ihre Produkt- und Assembly-Version aus
  `version.txt` über `Directory.Build.props`.
- Der Windows-Installer erhält seine Version beim Build aus `version.txt`; der
  Inno-Setup-Quelltext enthält keine eigene Produkt-Versionsquelle.
- `release.yml` startet nach einer Änderung an `version.txt` auf `main` oder
  bewusst manuell auf `main`.
- Der annotierte Tag `vX.Y.Z` wird erst nach erfolgreichen Windows- und
  Linux-Builds, Tests und Artefaktprüfungen auf den Merge-Commit gesetzt.
- Der veröffentlichte Release enthält Windows-EXE, Windows-ZIP, Installer,
  Linux-TAR.GZ und `SHA256SUMS.txt`.
- Release-Please, das Manifest und der separate Reparatur-Workflow wurden
  entfernt. Historische Changelog-Einträge bleiben erhalten.
- Automatische Löschung, Bereinigung und Prune von Restic-Daten bleiben
  ausgeschlossen.

## Zuletzt erledigt

- Direkten Release-Ablauf und PR-CI nach `main` umgesetzt.
- Veraltete Release-Automatisierung entfernt und Dokumentation aktualisiert.
- Version `0.3.6` für das nächste automatische Release vorbereitet.
- Installer-Versionsprüfung auf die zentrale Versionsquelle umgestellt.

## Offene Aufgaben

- PR #62 muss die erfolgreiche CI abwarten und nach `main` gemergt werden.
- Nach dem Merge den automatischen Tag `v0.3.6`, Release-Status, Artefakte und
  SHA-256-Prüfsummen remote verifizieren.
- Interne lokale Codex-Checkpoint-Referenzen unter
  `.git/refs/codex/turn-diffs` separat und nur ohne aktive Codex-Sitzung prüfen.

## Nächster Schritt

PR #62 nach erfolgreicher CI mergen. Danach erstellt `release.yml` automatisch
den getesteten und veröffentlichten Release `v0.3.6`.
