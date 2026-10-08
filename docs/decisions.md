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

## Nur lokale Verbindungen ab 3.0.0

Auf Benutzerwunsch entfällt auch REST einschließlich Zugangsdaten und Adressbau.
Gespeicherte REST-Profile werden wie SFTP/S3 beim Laden dauerhaft gelöscht.
Lokale Ordner und Netzlaufwerke bleiben erhalten; allgemeine Sitzungsvariablen bleiben verfügbar.
