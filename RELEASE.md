# Release-Ablauf

Restic Browser verwendet Release-Please mit dem automatisch bereitgestellten
secrets.GITHUB_TOKEN. Es werden keine GitHub Apps, Personal Access Tokens oder
zusätzlichen Repository-Secrets benötigt.

## Version und Artefakte

version.txt ist die zentrale Produktversion. Directory.Build.props stellt sie
für die Hauptanwendung und den Remote-Helfer bereit; daraus entstehen auch die
Assembly-, Datei- und Informational-Versionen. Der Inno-Setup-Fallback wird von
Release-Please mitgepflegt und der Installer-Build erhält die Version nochmals
explizit.

Ein vollständiger Release enthält exakt diese öffentlichen Namen:

- ResticBrowser.exe
- ResticBrowserWindows-<version>-win-x64.zip
- ResticBrowser-Setup.exe
- ResticBrowser-linux-x64.tar.gz
- SHA256SUMS.txt

Die ZIP- und TAR.GZ-Dateien enthalten jeweils die ausführbare Datei, README.md
und LICENSE. Dieses Repository ist eine .NET/Avalonia-Anwendung und nutzt weder
package.json, HACS, JavaScript-Code-Splitting noch Brotli-Dateien. Der
Artefaktprüfer lehnt unerwartete .js-, .br- oder sonstige zusätzliche Dateien
ab, damit nichts stillschweigend aus einem Release fehlt.

## Conventional Commits und Pull Requests

Commit- und PR-Titel folgen Conventional Commits:

- fix: erzeugt eine Patch-Version.
- feat: erzeugt eine Minor-Version.
- feat! oder ein BREAKING CHANGE: erzeugt eine Major-Version.
- docs:, test:, ci: und chore: dokumentieren Änderungen, lösen aber
  normalerweise keine Produktversion allein aus.

Änderungen gehen über einen Feature-Branch und einen Pull Request nach main.
Die CI läuft für Pull Requests nach main, für Pushes nach main und manuell.
main wird nicht direkt beschrieben.

## Release-Please-Ablauf

.github/workflows/release.yml läuft nach Pushes auf main oder manuell auf
main und verwendet den bei GitHub verifizierten Release-Please-v5-Commit.
Release-Please hält einen Release-PR aktuell. Nach dessen Merge erstellt es den
Release als Draft. Der Workflow verwendet den exakt von Release-Please gelieferten
Tag-Namen, baut diesen Tag auf Windows und Ubuntu, führt Restore, Format-,
Paket-Sicherheits-, Versions-, Build-, Test- und Artefaktprüfungen aus und lädt
danach alle Artefakte in den Draft hoch. Die Remote-Namen, -Dateigrößen und
SHA-256-Hashes werden gegen den lokalen Manifeststand verglichen. Erst wenn
diese Prüfung erfolgreich ist, wird der Draft als latest veröffentlicht; danach
erfolgt eine zweite Remote-Prüfung mit erneutem Download und Hashvergleich.

Ein Draft-Release erhält von GitHub normalerweise zunächst noch keinen Git-Ref.
Die Konfiguration aktiviert deshalb Release-Please force-tag-creation. Damit
legt Release-Please den ausgegebenen Tag gezielt auf den ausgegebenen Commit an;
die Build-Jobs checken genau diesen Tag aus. Tag-Name und Commit werden nicht aus
einer freien Eingabe oder einer alten Version abgeleitet.

Es existiert kein nach release.published gestarteter Upload-Workflow.

## Notwendige Repository-Einstellungen

Unter Settings → Actions → General → Workflow permissions müssen aktiviert
sein:

- Read and write permissions
- sofern angeboten: Allow GitHub Actions to create and approve pull requests

Der Default-Branch bleibt main. Branch-Schutzregeln sollten erfolgreiche
CI-Prüfungen und einen Pull Request verlangen. Der Workflow verwendet für
Release-Please, GitHub-API, Upload und Veröffentlichung ausschließlich
secrets.GITHUB_TOKEN.

## Manueller Reparaturablauf

.github/workflows/repair-release.yml wird über workflow_dispatch gestartet.
Es sind ein vorhandener Release-Tag wie v0.3.4 und Publish anzugeben.
Publish steht standardmäßig auf false. Der Workflow prüft, dass der Tag auf
main liegt, checkt den Tag aus, baut Windows und Linux neu, prüft die vollständige
Artefaktmenge, lädt sie hoch und verifiziert sie remote. Nur bei Publish=true
wird ein Draft veröffentlicht; bei false bleibt ein neu angelegter Release
Draft.

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
