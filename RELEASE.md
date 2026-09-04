# Release-Ablauf

## Version und Artefakte

`version.txt` ist die zentrale Produktversion. `Directory.Build.props` stellt
sie für die Hauptanwendung und den Remote-Helfer bereit; daraus entstehen auch
die Assembly-, Datei- und Informational-Versionen. Der Installer erhält die
Version beim Build explizit und enthält zusätzlich einen geprüften Fallback.

Ein vollständiger Release enthält exakt diese öffentlichen Namen:

- `ResticBrowser.exe`
- `ResticBrowserWindows-<version>-win-x64.zip`
- `ResticBrowser-Setup.exe`
- `ResticBrowser-linux-x64.tar.gz`
- `SHA256SUMS.txt`

Die ZIP- und TAR.GZ-Dateien enthalten jeweils die ausführbare Datei, `README.md`
und `LICENSE`. Der Artefaktprüfer lehnt unerwartete zusätzliche Dateien ab.

## Arbeitsablauf

Änderungen gehen über einen Feature-Branch und einen Pull Request nach `main`.
Die PR-CI läuft auf Windows und Ubuntu. `main` wird nicht direkt beschrieben.

Für ein neues Release:

1. `version.txt` auf eine neue semantische `MAJOR.MINOR.PATCH`-Version setzen.
2. Änderung per Pull Request nach `main` mergen.
3. Die Änderung an `version.txt` startet automatisch
   `.github/workflows/release.yml`.
4. Der Workflow prüft Version, Übersetzungen, Formatierung,
   Paket-Sicherheitsstatus, Build und Tests auf Windows und Linux.
5. Nach erfolgreicher Artefaktprüfung wird der annotierte Tag `vX.Y.Z` exakt auf
   den Merge-Commit gesetzt.
6. Danach wird der GitHub Release mit GitHub-generierten Release Notes und allen
   geprüften Artefakten veröffentlicht.

FÃ¼r den einmaligen Bootstrap des direkten Release-Ablaufs oder eine bewusst
gestartete Wiederholung kann `release.yml` auf `main` Ã¼ber `workflow_dispatch`
manuell gestartet werden. Manuelle Starts von Feature-Branches werden abgelehnt;
der regulÃ¤re Ablauf bleibt die Ã„nderung von `version.txt`.

Der Release-Job besitzt als einziger Job `contents: write`. Alle Build-Jobs
arbeiten mit Leserechten. Es werden ausschließlich `secrets.GITHUB_TOKEN` und
die vorinstallierte GitHub CLI verwendet; zusätzliche Secrets oder GitHub Apps
sind nicht erforderlich.

Der Workflow prüft vorhandene Tags. Ein Tag auf einem anderen Commit beendet
den Lauf mit Fehler. Ein bereits korrekt vorhandener Tag wird bei einer
Wiederholung wiederverwendet. Ein vorhandener Draft kann nach erneutem Upload
veröffentlicht werden; ein bereits veröffentlichter Release wird nur noch
remote gegen die lokalen Artefakte verifiziert.

## Release-Notizen und Historie

Neue Release Notes werden direkt von GitHub aus den Änderungen seit dem
vorherigen Tag erzeugt. Die historische `CHANGELOG.md` bleibt erhalten und
wird nicht automatisch verändert.

## Artefaktprüfung

Vor der Veröffentlichung wird die vollständige Menge der Artefakte lokal
geprüft und `SHA256SUMS.txt` erstellt. Nach der Veröffentlichung lädt
`scripts/verify-remote-release.ps1` den Release erneut herunter und vergleicht
Namen, Größen und SHA-256-Hashes.

## Lokale Prüfkommandos

Der vollständige Windows-Release-Check lautet:

```powershell
pwsh -NoProfile -File .\scripts\verify-release.ps1
```

Mit installiertem Inno Setup kann der Installer verpflichtend gemacht werden:

```powershell
pwsh -NoProfile -File .\scripts\verify-release.ps1 -RequireInstaller
```

Die Einzelprüfungen entsprechen dem CI-Gate:

```powershell
dotnet restore ResticBrowser.slnx
dotnet format --verify-no-changes
dotnet list ResticBrowser.slnx package --vulnerable --include-transitive
pwsh -NoProfile -File .\scripts\verify-version.ps1
pwsh -NoProfile -File .\scripts\verify-translations.ps1
dotnet build ResticBrowser.slnx -c Release --no-restore
dotnet run --project tests/ResticBrowser.Tests/ResticBrowser.Tests.csproj -c Release --no-build
```

Die Linux-Paketierung und der GUI-Starttest laufen auf Ubuntu in CI:

```sh
./scripts/publish-linux.sh Release
```
