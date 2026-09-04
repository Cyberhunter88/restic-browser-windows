# Project State

## Aktuelle Aufgabe

Git- und Release-Ablauf bereinigen und automatische Veröffentlichungen über
`version.txt` aufbauen.

## Aktueller Status

- Branch: `feature/direct-version-release-cleanup`.
- Produktversion: `0.3.5` zentral in `version.txt`; Haupt-App, Remote-Helfer und
  Installer übernehmen diese Version.
- Der Release-Workflow ist auf direkte Releases nach einer Änderung an
  `version.txt` auf `main` umgestellt.
- Windows- und Linux-Release-Builds prüfen Version, Übersetzungen, Formatierung,
  Paket-Sicherheitsstatus, Build, Tests und Produktionsartefakte.
- Der Tag `vX.Y.Z` wird erst nach erfolgreichen Builds auf den geprüften
  Merge-Commit gesetzt.
- GitHub veröffentlicht danach einen nicht-Draft- und nicht-Pre-Release mit
  `ResticBrowser.exe`, Windows-ZIP, Installer, Linux-TAR.GZ und
  `SHA256SUMS.txt`.
- Release-Please, das Manifest und der manuelle Reparatur-Workflow wurden
  entfernt. Historische Changelog-Einträge bleiben erhalten.
- Avalonia, .NET 10, Windows x64 und Linux x64.
- Snapshot-Auswahl, Vorschau, Suche, Restore, Vergleich, Speicheranalyse,
  Linux-VPS-Restore und Linux-Mount sind dokumentiert.
- Automatische Löschung, Bereinigung und Prune bleiben ausgeschlossen.

## Zuletzt erledigt

- Aktuellen Feature-Stand read-only mit `main`, Status und Release-Dokumentation
  abgeglichen.
- Eigenen Feature-Branch für die Workflow-Bereinigung erstellt.
- Release-Workflow auf `version.txt` als Auslöser und direkten Tag-/Release-
  Ablauf umgestellt.
- Doppelte Main-CI-Ausführung entfernt und veraltete Release-Konfigurationen
  gelöscht.
- Release-Dokumentation und Changelog-Hinweis aktualisiert.

## Offene Aufgaben

- Lokale Format-, Sicherheits-, Build- und Testprüfungen ausführen.
- YAML-/Workflow-Validierung ausführen.
- Bei erreichbarem GitHub `v0.3.5` prüfen und nur als verwaisten Test-Draft
  gezielt entfernen.
- Pull Request erstellen und nach dem Merge den automatischen Tag sowie den
  veröffentlichten Release remote verifizieren.

## Nächster Schritt

Lokale Prüfungen abschließen, die Workflow-Änderungen als Pull Request nach
`main` bringen und den ersten direkten Release-Lauf beobachten. Die internen
lokalen Codex-Checkpoint-Refs unter `.git/refs/codex/turn-diffs` werden separat
und nur ohne aktive Codex-Sitzung bereinigt.
