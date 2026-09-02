# Entscheidungen

## Restic bleibt die einzige Repository-Schnittstelle

Die Anwendung verwendet Restic für den Zugriff auf Backup-Repositories. Der
normale Workflow bleibt lesend; automatische Lösch- und Bereinigungsfunktionen
sind ausgeschlossen. Destruktive Aktionen benötigen eine separate ausdrückliche
Anforderung und die projektspezifischen Schutzmaßnahmen.
