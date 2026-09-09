# Architektur

- Portable Avalonia-Anwendung für Windows x64 und Linux x64.
- Restic ist die einzige Schnittstelle zum Backup-Repository.
- Die Anwendung bündelt Snapshot-Auswahl, Vorschau, Suche, Restore, Vergleich
  und Speicheranalyse.
- Zusätzliche Flows decken Linux-VPS-Restore und Linux-Mount ab.
- Restic bleibt ein separater, über `ProcessStartInfo.ArgumentList` gestarteter Prozess. Die Windows-Binärdatei ist als geprüfte Ressource eingebettet; Linux liefert sie im Paket unter `tools/restic` aus.
- Profile speichern keine Backend-Geheimnisse. S3-/REST-Zugangsdaten und Passwörter bleiben in `SessionCredentials` und werden nur an den jeweiligen Restic-Prozess vererbt.

## Zuständigkeiten

Das MainViewModel bleibt der Datenkontext des Hauptfensters. Verbindung,
Snapshots, Navigation und Restore liegen in getrennten Partial-Dateien.
NavigationHistory besitzt den Verlauf, SnapshotFilter den Suchindex und die
Filterlogik, RestoreCoordinator erstellt lokale und entfernte Restore-Aufträge.
Die Komponenten speichern keine zusätzlichen Zugangsdaten.

ResticRepositoryService liefert Snapshots und Suchtreffer über asynchrone
Batch-Callbacks. ResultBatch bündelt bis zu 256 Einträge; der erste Eintrag wird
sofort geliefert. FindMatchReader liest Treffer auch innerhalb einer einzelnen
Snapshot-Gruppe aus dem JSON-Stream. Erwartete Callback-Abschlüsse bremsen den
Produzenten, statt eine unbegrenzte Warteschlange zur Oberfläche aufzubauen.
Die fertige Snapshot-Liste wird nach Zeit sortiert. Die Suchgrenze von 10.000
Einträgen bleibt bestehen. Veraltete oder abgebrochene Vorgänge übernehmen
keine weiteren Batches; fehlerhafte Teilergebnisse werden entfernt.

Compiled Bindings sind projektweit Standard. Tabellen und Templates deklarieren
ihre jeweiligen Modelltypen; Fensterlayout und Theme-Ressourcen bleiben gleich.

## Tests und CI

Der bestehende Konsolen-Testaufruf bleibt erhalten. Program.cs registriert die
Tests; Core, Restore, Remote, Performance, Streaming und Integration besitzen
eigene Testdateien. Fakes und Assertions sind getrennt. Fehlende Plattformen
und fehlende SSH-E2E-Konfiguration werden als SKIP ausgewiesen.

CI und Release verwenden lokale Composite Actions unter `.github/actions`
für .NET-Setup, Restic-Installation sowie Build und Tests. Trigger, Release-Jobs,
Artefaktprüfungen und Schreibberechtigungen bleiben in den Workflows.
