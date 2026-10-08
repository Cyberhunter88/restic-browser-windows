# Entscheidungen

## Restic bleibt die einzige Repository-Schnittstelle

Die Anwendung verwendet Restic für den Zugriff auf Backup-Repositories. Der
normale Workflow bleibt lesend; automatische Lösch- und Bereinigungsfunktionen
sind ausgeschlossen. Destruktive Aktionen benötigen eine separate ausdrückliche
Anforderung und die projektspezifischen Schutzmaßnahmen.

## Schlanker Funktionsumfang ab 2.0.0

SFTP, S3/MinIO, Textversionsvergleich, Snapshot-Vergleich und Sitzungsdiagnose
entfallen. Gespeicherte SFTP- und S3-Profile werden beim Laden dauerhaft entfernt.
Lokale und REST-Repositories sowie Vorschau, Dateiversionen und Restore bleiben erhalten.
