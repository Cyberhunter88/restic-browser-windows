# Project State

## Aktuelle Aufgabe

Restic Browser 1.0.3 bereinigt Build-Altlasten und führt die Produktversion zentral in Directory.Build.props.

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

## Aktuelle Änderung 1.0.3

- version.txt entfällt; ResticBrowserProductVersion in Directory.Build.props steuert Build, Installer und Releases.
- Automatische Releases vergleichen die Produktversion vor und nach einem Push; manuelle Starts auf main bleiben erhalten.
- Erzeugte Build-Ausgaben und der abgeschlossene Optimierungsplan werden entfernt; aktive Paket- und Signaturprüfungen bleiben erhalten.

- Lokal am 5. Oktober 2026 bestanden: Release-Build, Formatprüfung, NuGet-Schwachstellenprüfung, 49/49 Tests, PowerShell-Syntax und Versions-/Release-Szenarien.
- Portable Windows-EXE gebaut; Restic-Signatur und EXE-Version 1.0.3 geprüft. Installer offen: Inno Setup fehlt. Linux-CI offen: kein Push beauftragt.

### Vorherige Änderung 1.0.2

- Patch-Version 1.0.2: gemeinsame Prüfung des Linux-Archivs auf Ubuntu und aktuellem Arch Linux in CI und Release; kein systemweites .NET oder Restic im Arch-Test.
- Paketprüfung startet die GUI unter Xvfb ohne Root-Rechte auf Arch, aus einem Pfad mit Leerzeichen und einem anderen Arbeitsverzeichnis; gebündeltes Restic wird über `version --json` gegen das Manifest geprüft.
- Linux-Paketierung bereitet Restic einmal vor; Release-Artefaktprüfung verlangt `tools/restic`.
- Lokal bestanden: Shell-Syntax, Workflow-YAML/Abhängigkeiten und acht Paketprüfer-Tests mit Fixture-Binärdateien (einschließlich Fehlerszenarien und Cleanup).
- CI am 1. Oktober 2026 für Commit `0f62698` erfolgreich: Formatierung, Paket-Schwachstellenprüfung, Versionsprüfung, Windows-/Linux-Builds und Tests sowie Paketstart auf Ubuntu und Arch unter Xvfb (GitHub Actions Run `36845974555`).
- Die Windows-Arbeitsumgebung wurde am 2. Oktober 2026 erneut geprüft: .NET SDK und PowerShell sind verfügbar; Docker und eine installierte WSL-Distribution stehen für den Arch-Desktop-Test nicht zur Verfügung.
- Offen: manuelle Arch-Desktop-Prüfung von Hauptfenster/Verbindungsdialog in Hell und Dunkel, Verbindung/Vorschau/Restore sowie Wayland über XWayland.
- Ausgangsstand ist der über PR #83 integrierte Stand 1.0.1. Der Arbeitsbaum war bei der Prüfung am 2. Oktober 2026 sauber; die früher vermerkten lokalen Zeilenendenänderungen sind nicht mehr offen.

### Vorherige Änderung 1.0.1

- Patch-Version 1.0.1: Dateiversionssuche beendet den Restic-Stream beim Trefferlimit.
- „Nur neueste Snapshots“ hält während des Ladens eine inkrementelle Projektion je Host und Pfad aktuell; beim Abschluss wird die Liste einmal sortiert.
- Die Restic-Bundle-Version bleibt 0.19.1. Die interne Bereinigungsprüfung ergab keine sichere private oder interne Entfernung; der ungenutzte öffentliche Runner-Wrapper bleibt aus Kompatibilitätsgründen bestehen.
