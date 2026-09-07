# Project State

## Aktuelle Aufgabe

Sechs vereinbarte Optimierungen umsetzen, siehe `docs/OPTIMIZATION-PLAN.md`.

## Aktueller Status

- Branch: `codex/project-optimizations`; Produktversion: `0.3.7`.
- GitHub-main-Commit `1a47e09` vom 7. September 2026 integriert.
- Vorbereitete Versionsänderung erhalten; alle Produkte beziehen `version.txt`.
- Upstream begrenzt Suchtreffer auf 10.000 und Ordneraggregation auf 100.000 Pfade.
- Tags und Releases bleiben ausschließlich Aufgabe von GitHub Actions.
- Interne Checkpoint-Referenz mit Endung `1787913869623/d01f5d0c-f149-495f-8b2f-82b72be358a5`
  verweist auf ein vorhandenes Tree-Objekt. Ursache war die Windows-Pfadlänge.
  Mit repository-lokalem `core.longpaths=true` funktionieren Referenzauflösung
  und normaler Fetch wieder. Keine interne Referenz wurde verändert.
- ViewModel in Themenbereiche aufgeteilt; Navigation, Filter und Restore-Aufträge
  besitzen eigene Komponenten.
- Compiled Bindings und schrittweise Snapshot-/Suchergebnisse implementiert.
- CI-Duplikate in drei lokale Composite Actions ausgelagert.
- Tests: 45 bestanden, 3 unter Windows übersprungen, 0 fehlgeschlagen.
  Release-Build erfolgreich; NuGet-Check ohne bekannte anfällige Pakete.
- Portable Windows-EXE gebaut und Versionsprüfung bestanden (`0.3.7`).
- YAML-Struktur und lokale Action-Verweise geprüft.
- Format-, Versions- und Git-Diff-Prüfung erfolgreich.

## Offene Aufgaben

- Native Linux-, OpenSSH- und GUI-Prüfungen sowie GitHub-CI gesondert nachweisen.
- Installer auf einem System mit Inno Setup prüfen; lokal nicht installiert.
- Änderungen als Pull Request veröffentlichen, wenn beauftragt.
