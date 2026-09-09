# Project State

## Aktuelle Aufgabe

Restic vollständig mitliefern und Wiederherstellung, Backend-Profile sowie Dateiversionen erweitern.

## Aktueller Status

- Branch: `codex/restic-bundle`; Produktversion: `0.3.8`.
- GitHub-main-Commit `d53dd59` mit Release `v0.3.7` integriert.
- Windows enthält Restic 0.19.1 als eingebettete, beim ersten Einsatz hash-geprüft bereitgestellte Ressource. Linux liefert `tools/restic` im Archiv aus.
- Das Build-Manifest pinnt Archive, Hashes und den Signatur-Fingerprint. Die Paketvorbereitung verlangt GnuPG und prüft die offizielle Prüfsummen-Signatur.
- Der lokale Restore-Dialog bietet eine unveränderliche `--dry-run --json --verbose=2`-Vorschau mit Ergebnissummen und sichtbarem Limit.
- S3/MinIO- und REST-Profile speichern nur Endpunktdaten; Zugangsdaten bleiben sitzungsgebunden.
- Dateiversionen lassen sich je Host oder über alle Hosts finden, vorschauen, wiederherstellen und als Text nebeneinander vergleichen.
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
- Tests: 48 bestanden, 3 unter Windows übersprungen, 0 fehlgeschlagen.
  Release-Build erfolgreich; der NuGet-Sicherheitsfeed war lokal nicht erreichbar (`NU1900`).

## Offene Aufgaben

- Native Linux-, OpenSSH- und GUI-Prüfungen sowie GitHub-CI nach dem Pull Request nachweisen.
- Paketierung benötigt lokal GnuPG für die vorgeschriebene Signaturprüfung; dies ist in GitHub Actions installiert.
- Installer auf einem System mit Inno Setup prüfen; lokal nicht installiert.
