# Entwicklung unter Windows und CachyOS

Vor jeder Sitzung `STATUS.md`, `PROJECT-STATE.md`, README, relevante `docs/`
und `git status --short --branch` lesen. Änderungen auf einem eigenen
Feature-Branch vornehmen; `main` nur per Pull Request ändern. Andere lokale
Änderungen und bestehende Konfigurationen erhalten.

## Einrichtung

Git, die stabile Rust-Toolchain (`rustup`), Python 3 und VS Code mit rust-analyzer sind auf beiden Systemen
erforderlich. Unter Windows die Befehle in PowerShell, unter CachyOS im
Terminal ausführen. Der Hook verwendet `/bin/sh` und Git; Git for Windows
liefert die Shell mit.

```sh
git config --local core.hooksPath .githooks
```

Vorhandene lokale `core.hooksPath`-Einstellungen erst prüfen und vorhandene
Hooks integrieren, bevor diese Einstellung geändert wird. Auf CachyOS den
Hook bei Bedarf mit `chmod +x .githooks/pre-push` ausführbar machen.

### Neuer Rechner

1. Git, Rust (`rustup`), PowerShell 7 (`pwsh`) und VS Code mit rust-analyzer
   installieren. Für die Skripte und Tests werden außerdem Python 3, Bash und
   Restic (ab 0.17.1) benötigt; die Linux-Paketprüfung braucht zusätzliche Systempakete
   gemäß `scripts/install-linux-ci-dependencies.sh`. GnuPG und bzip2 werden
   für die Restic-Paketierung gebraucht, Inno Setup nur für den Windows-Installer. Zusätzlich
   `cargo install --locked cargo-deny cargo-audit`; auf Linux außerdem Xvfb (`xorg-server-xvfb`/`xvfb`).
   Die CI ist die Referenz für die vollständige Plattformprüfung.
   Für die Prozess-Ende-Regressionen des Konsolen-Test-Runners muss Python 3
   unter Windows als `python`, unter Linux als `python3` im `PATH` verfügbar sein.
2. Das Repository von GitHub klonen und in VS Code öffnen. Vor einer Aufgabe
   `git status --short --branch`, `git branch --show-current`, README,
   `STATUS.md` und `PROJECT-STATE.md` prüfen.
3. `git config --show-origin --get core.hooksPath` prüfen. Wenn kein anderer
   Hook-Pfad eingerichtet ist, im Klon `git config --local core.hooksPath .githooks`
   setzen. Vorhandene Hooks zuerst integrieren; sie dürfen nicht ersetzt werden.
   Unter CachyOS `chmod +x .githooks/pre-push` ausführen. Die Syntaxprüfung
   `sh -n .githooks/pre-push` unter Windows in Git Bash ausführen. Mit
   `git config --get core.hooksPath` und der Syntaxprüfung die
   Einrichtung und Syntax prüfen; beim Push muss der Secret-Scan laufen.
   Ein fehlendes Prüfwerkzeug ist ein Fehler.
4. `cargo --version` und `cd rust && cargo build --locked` ausführen. Die versionierte `rust/Cargo.lock` hält
   auch transitive Versionen fest; CI und Paketierung verwenden `--locked` und dürfen sie nicht ändern. Bei
   bewussten Abhängigkeitsänderungen die Lockdatei gemeinsam mit den Projektänderungen prüfen und committen.
   Dieses Repository hat keine Node- oder Go-Projektabhängigkeiten.

### Neue Aufgabe und Wechsel zwischen Rechnern

Vor jedem Wechsel `git status --short --branch` prüfen. Bei lokalen Änderungen
erst die eigenen Dateien gezielt committen oder separat sichern. `git switch`,
`git pull` und `git reset` dürfen keine ungesicherten Änderungen verwerfen.
Keine fremden Änderungen stagen. Einen neuen Branch nur für eine neue Aufgabe
von aktuellem `origin/main` erstellen:

```sh
git fetch origin
git switch -c codex/mein-thema origin/main
```

Vor einem Rechnerwechsel passende Tests, Secret-Scan, `git diff --check` und
den Diff gegen `origin/main` prüfen; dann die eigenen Dateien gezielt committen
und den Feature-Branch mit gesetztem Upstream pushen. Commit und Push durch
Codex erfolgen nur nach ausdrücklicher Freigabe. Beispiel für den ersten Push:

```sh
git push -u origin codex/mein-thema
```

Auf dem zweiten Rechner:

```sh
git status --short --branch
git fetch origin
git switch --track origin/codex/mein-thema
git pull --ff-only
```

Falls der Branch dort schon existiert, nach Prüfung des Arbeitsbaums
`git switch codex/mein-thema` und `git pull --ff-only` verwenden. Meldet Git
abweichende Commits oder lokale Änderungen, anhalten und beide Seiten prüfen;
kein Force Push und kein Rebase veröffentlichter Commits. Uncommitted Changes
werden nicht durch GitHub synchronisiert.

### Nach dem Merge

Auf jedem Rechner zuerst den Arbeitsbaum prüfen, dann `git fetch origin`,
`git switch main` und `git pull --ff-only` ausführen. Eine neue Aufgabe erhält
einen neuen `codex/mein-thema`-Branch von `origin/main`. Den bisherigen Branch
bei noch offener Arbeit oder unklarer Synchronisierung behalten; Branches
nicht automatisch löschen. `main` nie direkt committen oder pushen.

### GitHub-Schutz

`main` ist geschützt und der CI-Check `build-and-test` ist erforderlich.
Unter **Settings → Branches** beziehungsweise **Settings → Rules → Rulesets**
regelmäßig prüfen, dass Pull Requests vor Änderungen an `main` verlangt werden,
der Check `build-and-test` vor dem Merge erfolgreich sein muss und direkte
Pushes sowie Force Pushes verhindert werden. Die CI startet bei PRs nach
`main`, Pushes auf `main` und manuell. Repository-Einstellungen nur nach
bewusster Entscheidung ändern; ein Release wird nur manuell angefordert.

## Vor Commit und Push

```sh
git status --short --branch
git diff --check
cd rust
cargo fmt --check
cargo clippy --workspace --all-targets --locked -- -D warnings
RESTIC_BROWSER_REQUIRE_RESTIC=1 cargo test --workspace --release --locked
cargo deny check licenses
cargo audit
cd ..
python3 scripts/verify-version.py
python3 -m unittest discover -s tests/scripts -p "test_*.py"
git diff --cached --stat
```

Unter Linux fasst `sh rust/scripts/linux-verify.sh [--package]` die Rust-Prüfungen samt Xvfb-Start zusammen.

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
