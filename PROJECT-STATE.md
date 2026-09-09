# Project State

## Aktuelle Aufgabe

Sechs vereinbarte Optimierungen umsetzen, siehe `docs/OPTIMIZATION-PLAN.md`.

## Aktueller Status

- Branch: `codex/performance-restic-bundle`; Produktversion: `0.3.8`.
- Ausgangspunkt ist `origin/main` mit dem optimierten Stand; unabhängige Restic-Bundle-
  Zusatzfunktionen aus anderen Branches wurden nicht übernommen.
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
- Die Performance- und Bundle-Änderungen sind implementiert. Der Release-Build, der Windows-
  Portable-Publish, der Linux-Paketaufbau und der lokale vollständige Test-Runner sind erfolgt.
- Die ausführbare Restic-Version wird beim Verbinden tatsächlich per `restic version --json`
  validiert. Windows provisioniert die eingebettete Datei erst bei Bedarf; Linux-Pakete enthalten
  `tools/restic`.
- Tests: 48 bestanden, 3 unter Windows übersprungen, 0 fehlgeschlagen. Formatprüfung,
  Versionsprüfung, `git diff --check` und NuGet-Schwachstellenprüfung waren erfolgreich.
- YAML-Struktur und lokale Action-Verweise geprüft.
- Format-, Versions- und Git-Diff-Prüfung erfolgreich.

## Offene Aufgaben

- Installer auf einem System mit Inno Setup prüfen; lokal nicht installiert.
- Native Linux-, OpenSSH- und interaktive GUI-Prüfungen sowie GitHub-CI gesondert nachweisen.
- Änderungen als Pull Request veröffentlichen, wenn beauftragt.
