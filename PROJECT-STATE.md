# Project State

## Aktuelle Aufgabe

Restic Browser 1.0.2 sichert die bestehende portable Linux-x64-Ausgabe für Arch Linux ab.

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

## Aktuelle Änderung

- Patch-Version 1.0.2: gemeinsame Prüfung des Linux-Archivs auf Ubuntu und aktuellem Arch Linux in CI und Release; kein systemweites .NET oder Restic im Arch-Test.
- Paketprüfung startet die GUI unter Xvfb ohne Root-Rechte auf Arch, aus einem Pfad mit Leerzeichen und einem anderen Arbeitsverzeichnis; gebündeltes Restic wird über `version --json` gegen das Manifest geprüft.
- Linux-Paketierung bereitet Restic einmal vor; Release-Artefaktprüfung verlangt `tools/restic`.
- Lokal bestanden: Shell-Syntax, Workflow-YAML/Abhängigkeiten und acht Paketprüfer-Tests mit Fixture-Binärdateien (einschließlich Fehlerszenarien und Cleanup).
- Arch-Laufzeitprüfung und CI sind lokal noch nicht bestätigt: .NET SDK, PowerShell und Docker fehlen in der aktuellen Umgebung.
- Offen: manuelle Arch-Desktop-Prüfung von Hauptfenster/Verbindungsdialog in Hell und Dunkel, Verbindung/Vorschau/Restore sowie Wayland über XWayland.
- Ausgangsstand: `codex/performance-optimizations-1.0.1`, Version 1.0.1, zahlreiche vorhandene reine Zeilenendenänderungen; diese bleiben auf dem neuen Feature-Branch erhalten.

### Vorherige Änderung 1.0.1

- Patch-Version 1.0.1: Dateiversionssuche beendet den Restic-Stream beim Trefferlimit.
- „Nur neueste Snapshots“ hält während des Ladens eine inkrementelle Projektion je Host und Pfad aktuell; beim Abschluss wird die Liste einmal sortiert.
- Die Restic-Bundle-Version bleibt 0.19.1. Die interne Bereinigungsprüfung ergab keine sichere private oder interne Entfernung; der ungenutzte öffentliche Runner-Wrapper bleibt aus Kompatibilitätsgründen bestehen.
