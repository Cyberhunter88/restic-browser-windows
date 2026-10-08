# Architektur

- Portable Avalonia-Anwendung für Windows x64 und Linux x64.
- Restic ist die einzige Schnittstelle zum Backup-Repository.
- Restic läuft als separater Prozess. Windows enthält eine signierte, hash-geprüfte Ressource,
  die erst beim ersten Bedarf atomar in den Benutzerdaten bereitgestellt wird; Linux liefert
  `tools/restic` im Paket mit. Externe Dateien bleiben eine ausdrückliche Override-Option.
- Die Anwendung bündelt Snapshot-Auswahl, Vorschau, Suche, Restore
  und Speicheranalyse.
- Linux-Mount bleibt ein getrenntes Werkzeug für das lokale Durchsuchen von Snapshots.
- Restic bleibt ein separater, über `ProcessStartInfo.ArgumentList` gestarteter Prozess. Die Windows-Binärdatei ist als geprüfte Ressource eingebettet; Linux liefert sie im Paket unter `tools/restic` aus.
- Profile speichern keine Backend-Geheimnisse. REST-Zugangsdaten und Passwörter bleiben in `SessionCredentials` und werden nur an den jeweiligen Restic-Prozess vererbt.

## Zuständigkeiten

Das MainViewModel bleibt der Datenkontext des Hauptfensters. Verbindung,
Snapshots, Navigation und Restore liegen in getrennten Partial-Dateien.
NavigationHistory besitzt den Verlauf, SnapshotFilter den Suchindex und die
Filterlogik, RestoreCoordinator erstellt ausschließlich lokale Restore-Aufträge.
Die Komponenten speichern keine zusätzlichen Zugangsdaten.

ResticRepositoryService liefert Snapshots, Suchtreffer und Verzeichniseinträge über asynchrone
Batch-Callbacks. ResultBatch bündelt bis zu 256 Einträge; der erste Eintrag wird
sofort geliefert. FindMatchReader liest Treffer auch innerhalb einer einzelnen
Snapshot-Gruppe aus dem JSON-Stream. Erwartete Callback-Abschlüsse bremsen den
Produzenten, statt eine unbegrenzte Warteschlange zur Oberfläche aufzubauen.
Die fertige Snapshot-Liste wird nach Zeit sortiert. Die Suchgrenze von 10.000
Einträgen bleibt bestehen. Veraltete oder abgebrochene Vorgänge übernehmen
keine weiteren Batches; fehlerhafte Teilergebnisse werden entfernt.

Während des Ladens hält der Filter „Nur neueste Snapshots“ die jeweils neueste
Version je Host und Pfad inkrementell aktuell. Ersetzte Einträge werden batchweise
aus der sichtbaren Liste entfernt. Nach Abschluss wird die Snapshot-Liste einmal
sortiert und der Filter auf die bereits sortierte Eingabe angewendet. Die
Dateiversionssuche beendet ihren `find`-Stream beim Limit von 10.000 Treffern.

Der Prozess-Runner kann JSON-Array- und `find`-Streams nach einem Trefferlimit kontrolliert
beenden. Dabei werden stdout und stderr ausgelesen, ein absichtlicher früher Abbruch wird als
solcher gekennzeichnet und nicht als Restic-Fehler bewertet. Ein normaler CancellationToken-
Abbruch bleibt ein echter Abbruch.

Compiled Bindings sind projektweit Standard. Tabellen und Templates deklarieren
ihre jeweiligen Modelltypen; Fensterlayout und Theme-Ressourcen bleiben gleich.

## Tests und CI

Der bestehende Konsolen-Testaufruf bleibt erhalten. Program.cs registriert die
Tests; Core, Restore, Performance, Streaming, ReleaseScript und Integration besitzen
eigene Testdateien. Fakes und Assertions sind getrennt. Fehlende Plattformen
werden als SKIP ausgewiesen. Der entfernte Remote-Helfer gehört nicht mehr zur Architektur.

CI und Release verwenden lokale Composite Actions unter `.github/actions`
für .NET-Setup, Restic-Installation sowie Build und Tests. Trigger, Release-Jobs,
Artefaktprüfungen und Schreibberechtigungen bleiben in den Workflows.

## Produktversion und Releases

ResticBrowserProductVersion in Directory.Build.props ist die einzige Produktversionsquelle.
Build, Installer und Release-Prüfung verwenden denselben Wert. Der Release-Workflow
vergleicht bei Pushes die Version mit dem vorherigen Commit; andere Build-Einstellungen
lösen keinen Release aus. Beim Übergang wird die frühere Versionsdatei ausschließlich
aus der Git-Historie gelesen. Manuelle Releases bleiben auf main beschränkt.

Es gibt keine dauerhaften Ergebnis-Caches und keine direkte Einbindung der Restic-Go-Bibliothek.
Die begrenzten Batches, LRU-Caches und Suchgrenzen bleiben erhalten.

## Funktionsumfang ab 2.0.0

Verbindungen unterstützen lokale und REST-Repositories. SFTP- und S3-Profile werden
beim Laden entfernt; ihre numerischen Typkennungen bleiben für die Migration reserviert.
Textversionsvergleich, Snapshot-Vergleich und Laufzeitdiagnose sind entfernt.
