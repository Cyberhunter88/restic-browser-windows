#!/usr/bin/env python3
"""Manual release requests and their continuation after a merged version PR."""
import json
import os
from pathlib import Path
import re
import subprocess

MARKER = "<!-- restic-browser:manual-release:v1 -->"
VERSION_FILE = "rust/Cargo.toml"
LOCK_FILE = "rust/Cargo.lock"
RELEASE_FILES = sorted([VERSION_FILE, LOCK_FILE])
WORKSPACE_CRATES = ("restic-app", "restic-core", "restic-testkit", "restic-ui")


def run(*args):
    return subprocess.check_output(args, text=True).strip()


def api(path):
    return json.loads(run("gh", "api", path))


def version_tuple(value):
    if not re.fullmatch(r"(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)", value, flags=re.ASCII):
        raise ValueError("Version muss MAJOR.MINOR.PATCH ohne v oder Zusatz sein.")
    parts = tuple(map(int, value.split(".")))
    if any(part > 65534 for part in parts):
        raise ValueError("Versionsbestandteile dürfen höchstens 65534 sein (Windows-Dateiversion).")
    return parts


def normalize_requested_version(value):
    version = value.strip()
    if version.startswith(("v", "V")):
        version = version[1:]
    try:
        version_tuple(version)
    except ValueError as error:
        raise ValueError("Bitte eine Version wie 3.0.4 oder v3.0.4 eingeben, ohne Vorabversionszusatz; jeder Bestandteil darf höchstens 65534 sein.") from error
    return version


def workspace_section(text):
    match = re.search(r"(?ms)^\[workspace\.package\]\s*\n(.*?)(?=^\[|\Z)", text)
    if not match:
        raise ValueError("Der Abschnitt [workspace.package] fehlt in der Versionsquelle.")
    return match


def product_version(text):
    section = workspace_section(text)
    found = re.findall(r'(?m)^version\s*=\s*"([^"]*)"\s*$', section[1])
    if len(found) != 1:
        raise ValueError("Genau eine zentrale Produktversion ist erforderlich.")
    version_tuple(found[0])
    return found[0]


def set_version(text, version):
    version_tuple(version)
    product_version(text)
    section = workspace_section(text)
    body = re.sub(r'(?m)^(version\s*=\s*")[^"]*(")\s*$', lambda m: m[1] + version + m[2], section[1], count=1)
    return text[:section.start(1)] + body + text[section.end(1):]


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


def authorized_pr(pr, repo, sha, version):
    return (
        pr.get("merged_at") is not None
        and pr.get("merge_commit_sha") == sha
        and pr.get("base", {}).get("ref") == "main"
        and pr.get("head", {}).get("ref") == f"release/v{version}"
        and (pr.get("head", {}).get("repo") or {}).get("full_name") == repo
        and pr.get("user", {}).get("login") == "github-actions[bot]"
        and MARKER in (pr.get("body") or "")
    )


def output(version, sha, release):
    with open(os.environ["GITHUB_OUTPUT"], "a", encoding="utf-8") as stream:
        stream.write(f"version={version}\ntag_name=v{version}\nsha={sha}\nshould_release={str(release).lower()}\n")


def summary(text):
    with open(os.environ["GITHUB_STEP_SUMMARY"], "a", encoding="utf-8") as stream:
        stream.write(text + "\n")


def prepare_pr(repo, version, sha, text):
    branch = f"release/v{version}"
    prs = json.loads(run("gh", "pr", "list", "--repo", repo, "--head", branch,
                         "--base", "main", "--state", "open", "--json", "number,url,author,body"))
    if prs:
        pr = prs[0]
        if api(f"repos/{repo}/pulls/{pr['number']}")["user"]["login"] != "github-actions[bot]" or MARKER not in pr["body"]:
            raise ValueError("Der vorhandene Release-PR stammt nicht aus diesem Workflow.")
        run("git", "fetch", "origin", branch)
        if product_version(run("git", "show", f"FETCH_HEAD:{VERSION_FILE}")) != version:
            raise ValueError("Der vorhandene Release-Branch enthält eine andere Version.")
    else:
        existing = run("git", "ls-remote", "--heads", "origin", f"refs/heads/{branch}")
        if existing:
            # Resume if pushing succeeded but creating the PR failed. Never force-push.
            run("git", "fetch", "origin", branch)
            base = run("git", "merge-base", sha, "FETCH_HEAD")
            base_text = run("git", "show", f"{base}:{VERSION_FILE}")
            candidate = run("git", "show", f"FETCH_HEAD:{VERSION_FILE}")
            base_lock = run("git", "show", f"{base}:{LOCK_FILE}")
            candidate_lock = run("git", "show", f"FETCH_HEAD:{LOCK_FILE}")
            changed = run("git", "diff", "--name-only", f"{base}...FETCH_HEAD")
            author = run("git", "show", "-s", "--format=%ae", "FETCH_HEAD")
            if (changed.splitlines() != RELEASE_FILES
                    or candidate.strip() != set_version(base_text, version).strip()
                    or candidate_lock.strip() != set_lock_version(base_lock, version).strip()
                    or author != "41898282+github-actions[bot]@users.noreply.github.com"):
                raise ValueError("Der vorhandene Release-Branch wird nicht überschrieben; Inhalt oder Autor ist unerwartet.")
        else:
            run("git", "switch", "-c", branch, sha)
            Path(VERSION_FILE).write_text(set_version(text, version), encoding="utf-8")
            lock = Path(LOCK_FILE)
            lock.write_text(set_lock_version(lock.read_text(encoding="utf-8"), version), encoding="utf-8")
            run("git", "add", "--", *RELEASE_FILES)
            run("git", "-c", "user.name=github-actions[bot]", "-c",
                "user.email=41898282+github-actions[bot]@users.noreply.github.com",
                "commit", "-m", f"Release {version} vorbereiten")
            run("git", "push", "origin", f"HEAD:refs/heads/{branch}")
        body = (f"{MARKER}\n\nManuell in Actions angeforderter Release **{version}**.\n\n"
                "Nach dem Merge und erfolgreicher Main-CI veröffentlicht der Release-Workflow "
                "diese Version automatisch. Nur die zentrale Produktversion (Cargo.toml und Cargo.lock) wird geändert.\n\n"
                "Die CI wird zusätzlich ausdrücklich gestartet; ein durch GITHUB_TOKEN "
                "erzeugtes PR-Ereignis kann eine Freigabe erfordern.")
        body_file = Path(os.environ["RUNNER_TEMP"]) / "release-pr.md"
        body_file.write_text(body, encoding="utf-8")
        url = run("gh", "pr", "create", "--repo", repo, "--base", "main", "--head", branch,
                  "--title", f"Release {version} vorbereiten", "--body-file", str(body_file))
        pr = {"url": url, "number": int(url.rsplit("/", 1)[-1])}
    files = api(f"repos/{repo}/pulls/{pr['number']}/files?per_page=100")
    if sorted(entry["filename"] for entry in files) != RELEASE_FILES:
        raise ValueError("Ein Release-PR darf nur rust/Cargo.toml und rust/Cargo.lock ändern.")
    # workflow_dispatch also runs with GITHUB_TOKEN and supplies the required check.
    run("gh", "workflow", "run", "build.yml", "--repo", repo, "--ref", branch)
    summary(f"## Release {version} angefordert\n\nVersions-PR: {pr['url']}\n\n"
            "Bitte nach erfolgreicher CI mergen. Danach wird automatisch veröffentlicht. "
            "Bei Konflikten den PR aktualisieren; kein Release wird vorher erstellt.")


def main():
    repo = os.environ["GITHUB_REPOSITORY"]
    event = json.loads(Path(os.environ["GITHUB_EVENT_PATH"]).read_text())
    text = Path(VERSION_FILE).read_text(encoding="utf-8")
    current = product_version(text)
    sha = run("git", "rev-parse", "HEAD")
    if os.environ["GITHUB_EVENT_NAME"] == "workflow_run":
        workflow = event["workflow_run"]
        if (workflow["conclusion"] != "success" or workflow["event"] != "push"
                or workflow["head_branch"] != "main" or workflow["head_sha"] != sha
                or workflow["head_repository"]["full_name"] != repo):
            raise ValueError("Keine erfolgreiche Main-CI für den gewählten Commit.")
        prs = api(f"repos/{repo}/commits/{sha}/pulls?per_page=100")
        requested = [pr for pr in prs if authorized_pr(pr, repo, sha, current)]
        if not requested:
            output(current, sha, False)
            summary("Kein manuell angeforderter Versions-PR; keine Veröffentlichung.")
            return
        files = api(f"repos/{repo}/pulls/{requested[0]['number']}/files?per_page=100")
        if sorted(entry["filename"] for entry in files) != RELEASE_FILES:
            raise ValueError("Der Versions-PR enthält unerwartete Änderungen.")
        version = current
    else:
        if os.environ["GITHUB_REF"] != "refs/heads/main":
            raise ValueError("Releases dürfen nur auf main gestartet werden.")
        version = normalize_requested_version(os.environ["REQUESTED_VERSION"])
        if version_tuple(version) < version_tuple(current):
            raise ValueError("Die angeforderte Version ist älter als die Produktversion auf main.")
        if api(f"repos/{repo}/git/ref/heads/main")["object"]["sha"] != sha:
            raise ValueError("main wurde inzwischen geändert. Workflow erneut starten.")
        tags = run("git", "tag", "--list", "v*").splitlines()
        versions = [version_tuple(tag[1:]) for tag in tags
                    if re.fullmatch(r"v(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)", tag)]
        if versions and version_tuple(version) < max(versions):
            raise ValueError("Die angeforderte Version ist älter als ein vorhandener Release-Tag.")
        if f"v{version}" in tags and run("git", "rev-list", "-n", "1", f"v{version}^{{commit}}") != sha:
            raise ValueError("Der vorhandene Release-Tag gehört zu einem anderen Commit. Neue Version wählen.")
        if version != current:
            prepare_pr(repo, version, sha, text)
            output(version, sha, False)
            return
        runs = api(f"repos/{repo}/actions/workflows/build.yml/runs?head_sha={sha}&per_page=100")["workflow_runs"]
        if not any(item["head_sha"] == sha and item["head_branch"] == "main"
                   and item["event"] in ("push", "workflow_dispatch")
                   and item["status"] == "completed" and item["conclusion"] == "success"
                   for item in runs):
            raise ValueError("Für diesen Main-Commit fehlt erfolgreiche CI. CI abwarten oder starten und Release erneut anfordern.")
    output(version, sha, True)
    summary(f"## Release {version}\n\nGeprüfte Quelle: `{sha}`. "
            "Veröffentlichung erfolgt erst nach erfolgreichen Release-Prüfungen.")


def cli():
    try:
        main()
    except ValueError as error:
        message = str(error)
        # GitHub workflow commands require escaping data to prevent extra commands.
        escaped = message.replace("%", "%25").replace("\r", "%0D").replace("\n", "%0A")
        print(f"::error::{escaped}")
        summary(f"## Release nicht gestartet\n\n{message}")
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(cli())
