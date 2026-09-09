# Project State

## Aktuelle Aufgabe

Restic-Performance verbessern, die geprüfte Version mitliefern und Wiederherstellung, Backend-Profile sowie Dateiversionen weiterführen.

## Aktueller Status

- Branch: `codex/performance-restic-bundle`; Produktversion: `0.3.8`.
- `origin/main` enthält inzwischen den Restic-Bundle-Stand aus PR #74; dessen Backend-, Restore-
  und Dateiversionsfunktionen bleiben beim Konfliktabgleich erhalten.
- Windows enthält Restic 0.19.1 als eingebettete, beim ersten Einsatz hash-geprüft bereitgestellte Ressource. Linux liefert `tools/restic` im Archiv aus.
- Das Build-Manifest pinnt Archive, Hashes, den Signatur-Fingerprint und den öffentlichen Restic-Schlüssel. Die Paketvorbereitung verlangt GnuPG und prüft die offizielle Prüfsummen-Signatur ohne Keyserver-Abhängigkeit.
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
- Die Performance- und Bundle-Änderungen sind implementiert. Der Release-Build, der Windows-
  Portable-Publish, der Linux-Paketaufbau und der lokale vollständige Test-Runner sind erfolgt.
- Die ausführbare Restic-Version wird beim Verbinden tatsächlich per `restic version --json`
  validiert. Windows provisioniert die eingebettete Datei erst bei Bedarf; Linux-Pakete enthalten
  `tools/restic`.
- Beim Verbinden wird kein automatischer `stats`-Aufruf mehr gestartet; die Snapshot-Anzahl
  stammt direkt aus der geladenen Liste. Suche und neueste Dateiversion beenden Restic nach
  dem Trefferlimit beziehungsweise dem ersten gültigen Treffer.
- Tests: 48 bestanden, 3 unter Windows übersprungen, 0 fehlgeschlagen. Formatprüfung,
  Versionsprüfung, `git diff --check` und NuGet-Schwachstellenprüfung waren erfolgreich.
- YAML-Struktur und lokale Action-Verweise geprüft.
- Format-, Versions- und Git-Diff-Prüfung erfolgreich.
- Native Linux-, OpenSSH- und GUI-Prüfungen sowie GitHub-CI nach dem Pull Request nachweisen.
- Paketierung benötigt lokal GnuPG für die vorgeschriebene Signaturprüfung; dies ist in GitHub Actions installiert.
- Installer auf einem System mit Inno Setup prüfen; lokal nicht installiert.
- PR #75 nach dem Konfliktabgleich erneut durch CI prüfen lassen.
