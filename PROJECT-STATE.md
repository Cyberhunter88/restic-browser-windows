# Project State

## Aktuelle Aufgabe

Portable Restic-Anwendung mit lesendem Normalbetrieb und ausdrücklich geschützten
destruktiven Sonderaktionen weiterentwickeln.

## Aktueller Status

- Branch: `feature/linux-ubuntu-release`.
- HEAD: `369b213 App bereinigen und Version 0.3.3 setzen`.
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

- Vollständige .NET-Prüfungen erneut ausführen. Das lokale .NET SDK 10.0.400
  ist vorhanden, aber der Paket-Sicherheitsdatenabruf zu NuGet ist derzeit
  nicht möglich und festhängende lokale Compilerprozesse sperren eine
  temporäre Build-Datei.

## Nächster Schritt

Den Release-Build, die projektinternen Tests und den Ubuntu-Starttest in GitHub
Actions ausführen. Bereits erfolgreich geprüft: `git diff --cached --check` und
die Bash-Syntax von `scripts/publish-linux.sh`.
