# Entwicklung unter Windows und CachyOS

Vor jeder Sitzung `STATUS.md`, `PROJECT-STATE.md`, README, relevante `docs/`
und `git status --short --branch` lesen. Änderungen auf einem eigenen
Feature-Branch vornehmen; `main` nur per Pull Request ändern. Andere lokale
Änderungen und bestehende Konfigurationen erhalten.

## Einrichtung

Git, .NET 10 SDK und VS Code mit C#-Unterstützung sind auf beiden Systemen
erforderlich. Unter Windows die Befehle in PowerShell, unter CachyOS im
Terminal ausführen. Der Hook verwendet `/bin/sh` und Git; Git for Windows
liefert die Shell mit.

```sh
git config --local core.hooksPath .githooks
```

Vorhandene lokale `core.hooksPath`-Einstellungen erst prüfen und vorhandene
Hooks integrieren, bevor diese Einstellung geändert wird. Auf CachyOS den
Hook bei Bedarf mit `chmod +x .githooks/pre-push` ausführbar machen.

## Vor Commit und Push

```sh
git status --short --branch
git diff --check
dotnet format --verify-no-changes
dotnet build ResticBrowser.slnx -c Release
dotnet run --project tests/ResticBrowser.Tests/ResticBrowser.Tests.csproj -c Release --no-build
git diff --cached --stat
```

Vor jedem Push den Diff gegen `origin/main` und den Hook-Ausgang prüfen.
Dateien gezielt mit `git add <Pfad>` stagen. Der Hook prüft jede ausgehende
Commit-Version auf typische Zugangsdatenmuster. Bei einem Treffer die Datei
und den Commit untersuchen, Geheimnisse widerrufen und aus allen ausgehenden
Commits entfernen. Bereits veröffentlichte Historie nur nach ausdrücklicher
Freigabe ändern. `--no-verify` und andere Hook-Umgehungen sind untersagt.

Der lokale Scan ist eine zusätzliche Schranke und ersetzt keine Prüfung von
Binärdateien, Bildern oder ungewöhnlich kodierten Geheimnissen. Der bisherige
gemeinsame `Codex-GitSecretScanner.ps1` ist nicht Bestandteil dieses
Repositories; wenn er verfügbar ist, vor einem Push zusätzlich dessen
`Outgoing`- und `Published`-Prüfung ausführen. Sein Fehlen nicht als
erfolgreiche Ausführung ausgeben.
