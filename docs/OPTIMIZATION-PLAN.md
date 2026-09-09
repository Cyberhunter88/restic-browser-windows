# Performance- und Restic-Plan

## Zielstand 0.3.9

Der Feature-Branch `codex/performance-restic-bundle` bündelt die vereinbarten
Performance- und Restic-Änderungen. Unabhängige Funktionen aus anderen Branches,
insbesondere S3-/REST-Profile, Dateiversionen und Restore-Vorschau, gehören nicht
zu diesem Plan.

## Umgesetzt

- Der automatische `stats`-Aufruf beim Verbinden entfällt; die Oberfläche verwendet
  `Snapshots.Count`. `GetStatsAsync` bleibt als bewusste Aktion erhalten.
- `FindNewestAsync` beendet die lokale Suche nach dem ersten gültigen Dateitreffer.
  `FindAsync` beendet sie nach 10.000 Treffern und meldet die Kürzung.
- Der Prozess-Runner beendet Restic bei einem absichtlichen frühen Ende kontrolliert,
  leert die Ausgabekanäle und unterscheidet dies vom normalen Benutzerabbruch.
- Windows provisioniert die eingebettete Restic-Datei hash-geprüft, atomar und mit
  einer Sperre. Linux-Pakete enthalten `tools/restic`. Es gibt keine Laufzeit-Downloads.
- Die feste Priorität lautet: manuelle Auswahl, geprüfte Bundle-Version, portable
  Datei, Systempfad/PATH. Die tatsächliche Version wird beim Verbinden über
  `restic version --json` geprüft.
- Ein deaktivierter Command-Beobachter erfasst nur anonymisierte Laufzeitmetriken.
- Batch-Größe, LRU-Verzeichnis-Cache, UI-Virtualisierung und Ergebnisgrenzen bleiben
  bestehen; dauerhafte Ergebnis-Caches werden nicht eingeführt.

## Prüfung

Die Regressionen decken frühes Beenden, genau 10.000 Suchtreffer, saubere Prozess- und
Kanalschließung, Stats-Aufrufe, Resolver-Priorität, Hash- und Versionsprüfung sowie die
synthetischen Lastfälle mit 10.000 Snapshots und 100.000 Dateieinträgen ab. Die
plattformabhängigen Packaging- und Installer-Gates laufen in der jeweiligen CI-Umgebung.

## Bewusste Grenzen

Restic bleibt eine externe ausführbare Schnittstelle; die Restic-Go-Bibliothek wird nicht
in die .NET-Anwendung eingebettet. Der VPS-Restore verwendet unverändert das auf dem
Zielserver konfigurierte Restic. Tags und Releases werden ausschließlich von GitHub Actions
verwaltet.
