# Project State

## Aktuelle Aufgabe

Restic Browser wird für Version 1.0.0 auf lokales Durchsuchen und Wiederherstellen fokussiert.

## Zielumfang 1.0.0

- Repository-Profile für lokale Ordner, SFTP, S3/MinIO und REST bleiben erhalten.
- Snapshot-Auswahl, Suche, Vorschau, Dateiversionen, Text- und Snapshot-Vergleich,
  Speicheranalyse, lesende Integritätsprüfung und lokale Wiederherstellung bleiben erhalten.
- Linux-Mount bleibt als getrenntes lokales Werkzeug erhalten.
- VPS-Remote-Restore, SSH-Helfer, TAR-Export und Snapshot-Zeitachse sind entfernt.
- Große Verzeichnisse werden schrittweise angezeigt, vollständig sortiert und nur innerhalb
  des bestehenden begrenzten LRU-Caches zwischengespeichert.

## Prüfung

- Release-Build und lokaler Test-Runner werden auf dem Feature-Branch ausgeführt.
- Native Linux-, Mount-, Installer- und visuelle Theme-Prüfungen erfolgen in der passenden Umgebung.
