# Project State

## Aktuelle Aufgabe

Einheitlichen GitHub-CI- und Release-Ablauf mit zentraler SemVer-Versionsquelle
für die portable Restic-Anwendung einrichten.

## Aktueller Status

- Branch: `codex/unified-ci-release`.
- Produktversion: `0.3.5` in `version.txt`, Haupt-App, Remote-Helfer und Installer-Build.
- `version.txt` ist die zentrale Quelle für SemVer und die vier MSBuild-
  Versionseigenschaften beider .NET-Projekte.
- Avalonia, .NET 10, Windows x64 und Linux x64.
- Portable Linux-x64-Ausgabe mit ausführbarer Datei `ResticBrowser` und Ubuntu-GUI-Startprüfung in GitHub Actions.
- `build.yml` ist die reine CI für Pull Requests nach `main`, Pushes auf `main`
  und manuelle Starts.
- `release.yml` reagiert ausschließlich auf `version.txt`-Änderungen auf `main`
  oder einen manuellen Start auf `main`; `auto-tag.yml` wurde als konkurrierender
  Release-Mechanismus entfernt.
- Snapshot-Auswahl, Vorschau, Suche, Restore, Vergleich, Speicheranalyse,
  Linux-VPS-Restore und Linux-Mount sind dokumentiert.
- Automatische Löschung, Bereinigung und Prune bleiben ausgeschlossen.
- Restic-JSON verwendet Source-Generation; Suche und Ordneraggregation haben sichtbare Skalierungsgrenzen.

## Zuletzt erledigt

- Bestehende Workflows analysiert und CI/Release klar getrennt.
- Zentrale Versionsquelle, Versionskonsistenzprüfung, Artefaktprüfung und
  idempotentes Tag-/Release-Verhalten ergänzt.
- Pull-Request-Vorlage mit Summary, Changes, Testing, Version und Breaking
  Changes ergänzt.
- Suche auf 10.000 sichtbare Treffer und Ordneraggregation auf 100.000 Pfade begrenzt; CI-Preview-Artefakte laufen nur noch für Pull Requests.

## Offene Aufgaben

- Lokale Qualitätsprüfungen abschließen und die Änderungen für einen Pull
  Request nach `main` bereitstellen.

## Nächster Schritt

Nach dem lokalen Prüfprogramm den Feature-Branch als Pull Request nach `main`
einreichen. Das Tagging und die Veröffentlichung bleiben ausschließlich Aufgabe
von GitHub Actions nach einem Merge mit geänderter `version.txt`.
