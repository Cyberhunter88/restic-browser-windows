#!/usr/bin/env python3
"""Erzeugt eine Liste der Drittkomponenten (Name, Version, Lizenz, Quelle) für das Release-Paket.

Aufruf (aus der Wurzel des Repositorys): python3 scripts/third-party.py <zieldatei> [--target <triple>]
Grundlage ist `cargo metadata` für das Paket `restic-ui` (nur Abhängigkeiten, die in die Anwendung gelangen).
"""
import json
import subprocess
import sys


def main() -> int:
    if len(sys.argv) < 2:
        print("Aufruf: third-party.py <zieldatei> [--target <triple>]", file=sys.stderr)
        return 2
    out = sys.argv[1]
    command = ["cargo", "metadata", "--format-version", "1", "--locked"]
    if "--target" in sys.argv:
        command += ["--filter-platform", sys.argv[sys.argv.index("--target") + 1]]
    metadata = json.loads(subprocess.run(command, check=True, capture_output=True, text=True).stdout)
    packages = {p["id"]: p for p in metadata["packages"]}
    nodes = {n["id"]: n for n in metadata["resolve"]["nodes"]}
    root = next(p["id"] for p in metadata["packages"] if p["name"] == "restic-ui")
    seen, stack = set(), [root]
    while stack:
        current = stack.pop()
        if current in seen:
            continue
        seen.add(current)
        for dep in nodes[current]["deps"]:
            if all(kind["kind"] == "dev" for kind in dep["dep_kinds"]):
                continue
            stack.append(dep["pkg"])
    rows = []
    for package_id in seen:
        package = packages[package_id]
        if package["name"].startswith("restic-"):
            continue
        rows.append((package["name"], package["version"], package.get("license") or "unbekannt", package.get("repository") or ""))
    rows.sort(key=lambda row: (row[0].lower(), row[1]))
    with open(out, "w", encoding="utf-8", newline="\n") as handle:
        handle.write("# Drittkomponenten\n\n")
        handle.write("Restic Browser (Rust) verwendet die folgenden Open-Source-Komponenten.\n")
        handle.write("Die Lizenztexte stehen in den jeweiligen Quellen (Spalte Quelle).\n\n")
        handle.write("| Komponente | Version | Lizenz | Quelle |\n|---|---|---|---|\n")
        for name, version, license_, repository in rows:
            handle.write(f"| {name} | {version} | {license_} | {repository} |\n")
        handle.write("\nGebündelt: restic (BSD-2-Clause), https://github.com/restic/restic\n")
    print(f"{len(rows)} Komponenten -> {out}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
