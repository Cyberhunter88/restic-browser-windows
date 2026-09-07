# Optimierungsplan

Stand: 7. September 2026. Produktversion bleibt die vorbereitete `0.3.7`.

1. GitHub-main integrieren, Versionsänderung bewahren, beschädigte interne
   Referenz lesend untersuchen. Keine Tags oder Releases ändern.
2. Navigation, Snapshot-Filter und Restore in kleine Komponenten auslagern.
3. Testregistrierung, fachliche Tests, E2E und Fakes auf eigene Dateien verteilen.
   Bestehenden Aufruf erhalten; übersprungene Tests ausdrücklich kennzeichnen.
4. Compiled Bindings als Standard aktivieren, übrige Datenkontexte typisieren.
5. Snapshots und Suchtreffer in Batches anzeigen. Suchlimit, finale Sortierung,
   Auswahl, Abbruch und Schutz vor verspäteten Ergebnissen erhalten.
6. Wiederholte CI-Schritte in lokale Composite Actions auslagern; Trigger,
   Berechtigungen und Release-Prüfungen erhalten.
7. Format, Version, Paketprüfung, Release-Build, Tests und verfügbare Paketierung
   prüfen. Regressionstests für Batches vor Abschluss, Abbruch und Grenzen.
   Native Linux- und GUI-Prüfungen nur bei tatsächlicher Ausführung bestätigen.

## Ergebnis

- GitHub-main `1a47e09` integriert; Version `0.3.7` erhalten.
- Git-Referenzproblem als Windows-Pfadlängenproblem identifiziert und mit
  lokalem `core.longpaths=true` behoben. Normaler Fetch erfolgreich;
  keine interne Referenz gelöscht oder verändert.
- ViewModel nach Themen aufgeteilt; NavigationHistory, SnapshotFilter und
  RestoreCoordinator ausgelagert.
- Testdateien aufgeteilt und SKIP-Erfassung ergänzt.
- Compiled Bindings aktiviert, alle bisherigen dynamischen Tabellen-Bindings typisiert.
- Snapshot- und Such-Batches implementiert; acht neue Regressionstests.
- Drei gemeinsame Composite Actions integriert und YAML-Struktur geprüft.
- Release-Build erfolgreich; 45 Tests bestanden, 3 Linux-Tests übersprungen.
  NuGet-Check meldet keine bekannten anfälligen Pakete.
- Abschließende Formatprüfung und `git diff --check` erfolgreich.
- Portable Windows-EXE gebaut; Produkt-/Dateiversion als `0.3.7`/`0.3.7.0` geprüft.
- Installer-Prüfung wegen fehlendem Inno Setup nicht ausführbar.
- Native Linux-, OpenSSH- und interaktive GUI-Prüfungen sind lokal nicht ausgeführt.
  WSL ist nicht installiert. GitHub-CI wurde nicht gestartet.
