# Project State

## Aktuelle Aufgabe

Direkte, getestete GitHub-Releases über die zentrale Versionsquelle
`version.txt` bereitstellen.

## Aktueller Status

- Branch: `fix/release-tag-publish`.
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
- Der Release-Lauf `33854361835` prüfte Windows und Linux erfolgreich, scheiterte
  aber im Ubuntu-Sammeljob beim erneuten Lesen der Windows-PE-Dateiversion.
- Die Artefaktprüfung überspringt diese plattformfremde PE-Prüfung im
  Sammeljob jetzt ausdrücklich; der Windows-Build prüft sie weiterhin.
- Automatische Löschung, Bereinigung und Prune von Restic-Daten bleiben
  ausgeschlossen.

## Zuletzt erledigt

- Direkten Release-Ablauf und PR-CI nach `main` umgesetzt.
- Veraltete Release-Automatisierung entfernt und Dokumentation aktualisiert.
- Release-Workflow gegen das Überschreiben vorhandener Release-Assets gehärtet
  und auf den von GitHub bereitgestellten Token umgestellt.
- PR-Vorlage mit den verbindlichen Release- und Testabschnitten ergänzt.
- Version `0.3.6` für das nächste automatische Release vorbereitet.
- Installer-Versionsprüfung auf die zentrale Versionsquelle umgestellt.

## Offene Aufgaben

- Der Reparatur-Commit `e958312` muss per Pull Request nach `main` gebracht
  werden.
- Danach `release.yml` einmal bewusst auf `main` wiederholen, weil der Fix selbst
  `version.txt` nicht verändert. Anschließend Tag `v0.3.6`, Release-Status,
  Artefakte und SHA-256-Prüfsummen remote verifizieren.
- Interne lokale Codex-Checkpoint-Referenzen unter
  `.git/refs/codex/turn-diffs` separat und nur ohne aktive Codex-Sitzung prüfen.

## Nächster Schritt

Reparatur-PR nach erfolgreicher CI mergen und `release.yml` auf `main` starten.
Der Workflow erstellt danach automatisch den getesteten und veröffentlichten
Release `v0.3.6`; ein manueller Tag ist nicht erforderlich.
