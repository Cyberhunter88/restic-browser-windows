#!/usr/bin/env python3
"""Prüft die zentrale Produktversion (rust/Cargo.toml) gegen Cargo.lock, Tags, Installer und optional die EXE.

Aufruf: python3 scripts/verify-version.py [--tag vX.Y.Z] [--executable pfad]
"""
import argparse
import os
from pathlib import Path
import re
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[1]
VERSION_FILE = "rust/Cargo.toml"
LOCK_FILE = "rust/Cargo.lock"
WORKSPACE_CRATES = ("restic-app", "restic-core", "restic-testkit", "restic-ui")


def version_tuple(value):
    if not re.fullmatch(r"(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)", value, flags=re.ASCII):
        raise ValueError("Version muss MAJOR.MINOR.PATCH ohne v oder Zusatz sein.")
    parts = tuple(map(int, value.split(".")))
    if any(part > 65534 for part in parts):
        raise ValueError("Versionsbestandteile dürfen höchstens 65534 sein (Windows-Dateiversion).")
    return parts


def product_version(text):
    section = re.search(r"(?ms)^\[workspace\.package\]\s*\n(.*?)(?=^\[|\Z)", text)
    if not section:
        raise ValueError("Der Abschnitt [workspace.package] fehlt in der Versionsquelle.")
    found = re.findall(r'(?m)^version\s*=\s*"([^"]*)"\s*$', section[1])
    if len(found) != 1:
        raise ValueError("Genau eine zentrale Produktversion ist erforderlich.")
    version_tuple(found[0])
    return found[0]


def set_lock_version(text, version):
    """Setzt die Version der Workspace-Pakete in Cargo.lock; Fremdpakete bleiben unberührt."""
    version_tuple(version)
    count = 0
    for crate in WORKSPACE_CRATES:
        text, found = re.subn(
            rf'(?m)^(name = "{re.escape(crate)}"\nversion = ")[^"]*(")$',
            lambda m: m[1] + version + m[2], text,
        )
        count += found
    if count != len(WORKSPACE_CRATES):
        raise ValueError("Cargo.lock enthält nicht alle Workspace-Pakete genau einmal.")
    return text


def fail(message):
    print(f"Versionsprüfung fehlgeschlagen: {message}", file=sys.stderr)
    raise SystemExit(1)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--tag")
    parser.add_argument("--executable")
    args = parser.parse_args()

    try:
        version = product_version((ROOT / VERSION_FILE).read_text(encoding="utf-8"))
    except ValueError as error:
        fail(str(error))

    lock = (ROOT / LOCK_FILE).read_text(encoding="utf-8")
    try:
        if set_lock_version(lock, version) != lock:
            fail("Cargo.lock enthält für Workspace-Pakete eine andere Version als Cargo.toml.")
    except ValueError as error:
        fail(str(error))

    tags = subprocess.run(["git", "tag", "--list", "v*"], cwd=ROOT, capture_output=True, text=True).stdout.split()
    valid = [tag for tag in tags if re.fullmatch(r"v(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)", tag)]
    if valid and version_tuple(version) < max(version_tuple(tag[1:]) for tag in valid):
        fail(f"Version {version} ist älter als ein vorhandener SemVer-Tag.")

    for crate in (ROOT / "rust/crates").glob("*/Cargo.toml"):
        if not re.search(r"(?m)^version\.workspace\s*=\s*true\s*$", crate.read_text(encoding="utf-8")):
            fail(f"{crate.relative_to(ROOT)} verwendet nicht die zentrale Workspace-Version.")

    installer = (ROOT / "installer/windows/ResticBrowser.iss").read_text(encoding="utf-8")
    if not re.search(r"(?m)^\s*#ifndef\s+MyAppVersion\b", installer) or not re.search(r"(?m)^\s*#error\s+.*MyAppVersion", installer):
        fail("Der Installer muss MyAppVersion als Build-Definition erwarten.")
    if not re.search(r"(?m)^\s*AppVersion=\{#MyAppVersion\}", installer):
        fail("Der Installer verwendet MyAppVersion nicht für AppVersion.")

    if args.tag and args.tag != f"v{version}":
        fail(f"Tag {args.tag} stimmt nicht mit der zentralen Version v{version} überein.")

    if args.executable:
        # Pfad über die Umgebung übergeben: bei `pwsh -Command` landen Zusatzargumente nicht in $args.
        script = ("(Get-Item -LiteralPath $env:RB_EXECUTABLE).VersionInfo | "
                  "ForEach-Object { $_.FileVersion; $_.ProductVersion }")
        out = subprocess.run(["pwsh", "-NoProfile", "-Command", script], capture_output=True, text=True,
                             env={**os.environ, "RB_EXECUTABLE": str(Path(args.executable).resolve())})
        values = out.stdout.split()
        if out.returncode != 0 or values != [f"{version}.0", version]:
            fail(f"Die Dateiversion der EXE ist {values}, erwartet {[f'{version}.0', version]}. {out.stderr.strip()}")

    print(f"Versionsprüfung erfolgreich: {version}")


if __name__ == "__main__":
    main()
