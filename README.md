# Restic Browser

Eine portable Windows-Oberfläche zum Durchsuchen und Wiederherstellen vorhandener
[Restic](https://restic.net/)-Backups. Die Anwendung verändert keine Snapshots und
verwendet eine bereits installierte `restic.exe` ab Version 0.17.1.

## Downloads

[**Neueste portable ResticBrowser.exe herunterladen**](https://github.com/Cyberhunter88/restic-browser-windows/releases/latest/download/ResticBrowser.exe)

Die Anwendung ist eine selbstständige Windows-x64-Einzeldatei. Eine installierte
.NET-Runtime ist nicht erforderlich.

Alle veröffentlichten Versionen und Versionshinweise stehen unter
[GitHub Releases](https://github.com/Cyberhunter88/restic-browser-windows/releases).
Offizielle Windows-Binärdateien werden nach der Freischaltung durch SignPath
künftig digital signiert. Vertrauenswürdig sind ausschließlich Dateien aus diesem
offiziellen GitHub-Repository und dessen offiziellen GitHub Releases.

## Installation

Restic Browser ist portabel und besitzt keinen Installer. `ResticBrowser.exe` aus
dem offiziellen Release herunterladen oder das versionsbezogene ZIP-Archiv
entpacken und die EXE starten. Eine .NET-Runtime muss nicht installiert werden.
Eine kompatible `restic.exe` ab Version 0.17.1 muss separat installiert sein oder
wie unter [Nutzung vom USB-Stick](#nutzung-vom-usb-stick) beschrieben bereitliegen.

## Deinstallation

Zum Entfernen der Anwendung die heruntergeladene `ResticBrowser.exe` und
gegebenenfalls den entpackten Programmordner löschen. Restic Browser nimmt keine
systemweite Installation vor. Gespeicherte Repository-Profile bleiben dabei unter
`%LOCALAPPDATA%\ResticBrowser\settings.json` erhalten und können bei Bedarf
separat gelöscht werden. Passwörter werden dort nicht gespeichert.

## Funktionen

- lokale und entfernte Restic-Repositories
- Snapshot-Filter und dateisystemartige Navigation
- Suche im ausgewählten Snapshot
- Wiederherstellung einzelner oder mehrerer Dateien und Ordner
- vier sichere Überschreibmodi mit Fortschrittsanzeige und Abbruch
- Passwörter und Backend-Zugangsdaten nur im Arbeitsspeicher
- helle und dunkle deutsche Oberfläche

## Nutzung vom USB-Stick

`ResticBrowser.exe` ist selbstständig und kann direkt von einem USB-Stick gestartet
werden. Auf dem Zielrechner ist keine .NET-Runtime erforderlich.

Restic wird in dieser Reihenfolge automatisch gesucht:

1. `restic.exe` neben `ResticBrowser.exe`
2. `tools\restic.exe` neben der Anwendung
3. Einträge aus `PATH`
4. typische WinGet-Installationspfade

Für einen vollständig portablen Stick einfach `ResticBrowser.exe` und eine passende
Windows-x64-Version von `restic.exe` in denselben Ordner kopieren. Restic selbst wird
nicht mit dem Release gebündelt. Gespeicherte Repository-Profile liegen weiterhin
unter `%LOCALAPPDATA%\ResticBrowser` auf dem jeweiligen PC; Passwörter werden nie
gespeichert.

## Entwicklung

Voraussetzungen: Windows, .NET 10 SDK und optional Restic 0.17.1 oder neuer.

```powershell
dotnet build ResticBrowser.slnx
dotnet run --project tests/ResticBrowser.Tests
```

## Portable Einzeldatei

```powershell
dotnet publish src/ResticBrowser/ResticBrowser.csproj -c Release -r win-x64 --self-contained true
```

Die EXE liegt anschließend unter
`src\ResticBrowser\bin\Release\net10.0-windows\win-x64\publish\ResticBrowser.exe`.

Repository-Profile werden unter `%LOCALAPPDATA%\ResticBrowser\settings.json`
gespeichert. Passwörter und Umgebungsvariablen werden niemals dort abgelegt.

## Privacy

Restic Browser erhebt oder übermittelt keine Telemetrie. Die Anwendung greift nur
auf lokale oder entfernte Restic-Repositories und Speicherziele zu, die der
Benutzer ausdrücklich auswählt oder konfiguriert. Bei entfernten Backends führt
die separat installierte Restic-Anwendung die dafür erforderlichen
Netzwerkzugriffe aus.

## Code signing policy

See the [Code Signing Policy](CODE_SIGNING_POLICY.md).

Free code signing provided by [SignPath.io](https://signpath.io/), certificate by [SignPath Foundation](https://signpath.org/).

## Security

Sicherheitsprobleme bitte vertraulich über
[GitHubs private Sicherheitsmeldung](https://github.com/Cyberhunter88/restic-browser-windows/security/advisories/new)
melden und nicht als öffentliches Issue veröffentlichen.

## Lizenz

Restic Browser steht unter der [MIT-Lizenz](LICENSE).
