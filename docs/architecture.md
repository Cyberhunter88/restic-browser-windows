# Architektur

- Portable Avalonia-Anwendung für Windows x64 und Linux x64.
- Restic ist die einzige Schnittstelle zum Backup-Repository.
- Die Anwendung bündelt Snapshot-Auswahl, Vorschau, Suche, Restore, Vergleich
  und Speicheranalyse.
- Zusätzliche Flows decken Linux-VPS-Restore und Linux-Mount ab.
