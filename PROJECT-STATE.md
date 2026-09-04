# Project State

## Aktuelle Aufgabe

Portable Restic-Anwendung mit lesendem Normalbetrieb und ausdrücklich geschützten
destruktiven Sonderaktionen weiterentwickeln.

## Aktueller Status

- Branch: `feature/linux-ubuntu-release`.
- HEAD: `29b01fc Add Linux release packaging and validation`.
- Produktversion: `0.3.4` in Haupt-App, Remote-Helfer und Installer.
- Der Git-Index wurde mit dem vorhandenen Arbeitsbaum abgeglichen. Die
  irreführenden staged Löschmarkierungen und gleichnamigen untracked Dateien
  sind auf neun nachvollziehbare Projektänderungen reduziert.
- Avalonia, .NET 10, Windows x64 und Linux x64.
- Portable Linux-x64-Ausgabe mit ausführbarer Datei `ResticBrowser` und Ubuntu-GUI-Startprüfung in GitHub Actions.
- Snapshot-Auswahl, Vorschau, Suche, Restore, Vergleich, Speicheranalyse,
  Linux-VPS-Restore und Linux-Mount sind dokumentiert.
- Automatische Löschung, Bereinigung und Prune bleiben ausgeschlossen.

## Zuletzt erledigt

- Projektregeln, README, Status, Architektur und Entscheidungen geprüft.
- Architektur und Entscheidungen nach `docs\` übernommen.
- Linux-Projektpfade, TAR.GZ-Paketierung und Release-Artefaktprüfung ergänzt.

## Offene Aufgaben

- Den Branch auf GitHub pushen; der Ubuntu-Starttest läuft anschließend in
  GitHub Actions.

## Nächster Schritt

Den gepushten Branch in GitHub Actions prüfen. Erfolgreich geprüft: Formatierung,
Paket-Sicherheitsprüfung, Release-Build ohne Warnungen und 38/38 Tests.
