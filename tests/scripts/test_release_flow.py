"""Exercise release authorization and dispatch without network or publication."""
import copy
import importlib.util
import json
import os
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

SPEC = importlib.util.spec_from_file_location("release_flow", Path(__file__).resolve().parents[2] / "scripts/release-flow.py")
FLOW = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(FLOW)
XML = '<Project><PropertyGroup><ResticBrowserProductVersion>3.0.3</ResticBrowserProductVersion><ResticBrowserAssemblyVersion>$(ResticBrowserProductVersion).0</ResticBrowserAssemblyVersion></PropertyGroup></Project>'
REPO = "example/restic-browser"
SHA = "a" * 40
PR = {
    "number": 1, "merged_at": "2026-10-17T12:00:00Z", "merge_commit_sha": SHA,
    "base": {"ref": "main"}, "head": {"ref": "release/v3.0.3", "repo": {"full_name": REPO}},
    "user": {"login": "github-actions[bot]"}, "body": FLOW.MARKER,
}


class ReleaseFlowTests(unittest.TestCase):
    def test_versions(self):
        for invalid in ("", "v3.0.3", "03.0.3", "3.0", "3.0.3-beta", "3.0.3\n", "3.0.3;echo x", "65535.0.0", "٣.0.0"):
            with self.subTest(invalid=invalid), self.assertRaises(ValueError):
                FLOW.version_tuple(invalid)
        self.assertGreater(FLOW.version_tuple("3.0.10"), FLOW.version_tuple("3.0.9"))
        updated = FLOW.set_version(XML, "3.1.0")
        self.assertEqual(FLOW.product_version(updated), "3.1.0")
        self.assertIn('$(ResticBrowserProductVersion).0', updated)
        with self.assertRaises(ValueError):
            FLOW.product_version(XML.replace('</PropertyGroup>', '<ResticBrowserProductVersion>3.0.4</ResticBrowserProductVersion></PropertyGroup>'))

    def test_only_authorized_merged_request(self):
        self.assertTrue(FLOW.authorized_pr(PR, REPO, SHA, "3.0.3"))
        for field, value in (("merged_at", None), ("merge_commit_sha", "b" * 40), ("body", ""),
                             ("user", {"login": "someone"}), ("base", {"ref": "other"}),
                             ("head", {"ref": "release/v3.0.3", "repo": {"full_name": "fork/repo"}}),
                             ("head", {"ref": "release/v3.0.4", "repo": {"full_name": REPO}})):
            pr = copy.deepcopy(PR)
            pr[field] = value
            with self.subTest(field=field, value=value):
                self.assertFalse(FLOW.authorized_pr(pr, REPO, SHA, "3.0.3"))

    def invoke(self, event_name="workflow_dispatch", requested="3.0.3", prs=None,
               changed_files=None, conclusion="success", branch="main", source_event="push", ref="refs/heads/main", main_ci="success"):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            event = {"workflow_run": {"conclusion": conclusion, "event": source_event,
                                      "head_branch": branch, "head_sha": SHA,
                                      "head_repository": {"full_name": REPO}}}
            (root / "event.json").write_text(json.dumps(event))
            (root / FLOW.VERSION_FILE).write_text(XML)
            env = {"GITHUB_EVENT_NAME": event_name, "GITHUB_EVENT_PATH": str(root / "event.json"),
                   "GITHUB_REPOSITORY": REPO, "GITHUB_REF": ref, "REQUESTED_VERSION": requested,
                   "GITHUB_OUTPUT": str(root / "output"), "GITHUB_STEP_SUMMARY": str(root / "summary")}

            def api(path):
                if path.endswith("git/ref/heads/main"):
                    return {"object": {"sha": SHA}}
                if "/actions/workflows/" in path:
                    return {"workflow_runs": [{"head_sha": SHA, "head_branch": "main", "event": "push", "status": "completed", "conclusion": main_ci}]}
                if "/commits/" in path:
                    return [] if prs is None else prs
                if "/files" in path:
                    return [{"filename": name} for name in (changed_files or [FLOW.VERSION_FILE])]
                self.fail(path)

            def run(*args):
                if args == ("git", "rev-parse", "HEAD"):
                    return SHA
                if args == ("git", "tag", "--list", "v*"):
                    return "v3.0.2"
                self.fail(args)

            previous = Path.cwd()
            try:
                os.chdir(root)
                with patch.dict(os.environ, env), patch.object(FLOW, "api", side_effect=api), patch.object(FLOW, "run", side_effect=run), patch.object(FLOW, "prepare_pr") as prepare:
                    FLOW.main()
                    return (root / "output").read_text(), prepare.call_count
            finally:
                os.chdir(previous)

    def test_new_version_creates_pr_without_release(self):
        output, prepared = self.invoke(requested="3.1.0")
        self.assertIn("should_release=false", output)
        self.assertEqual(prepared, 1)

    def test_current_version_can_be_manually_released(self):
        output, prepared = self.invoke()
        self.assertIn("should_release=true", output)
        self.assertEqual(prepared, 0)

    def test_downgrade_and_non_main_dispatch_rejected(self):
        for kwargs in ({"requested": "3.0.2"}, {"ref": "refs/heads/feature"}):
            with self.subTest(kwargs=kwargs), self.assertRaises(ValueError):
                self.invoke(**kwargs)

    def test_prepare_request_and_explicit_ci_dispatch(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            previous = Path.cwd()
            commands = []

            def run(*args):
                commands.append(args)
                if args[:3] == ("gh", "pr", "list"):
                    return "[]"
                if args[:3] == ("git", "ls-remote", "--heads"):
                    return ""
                if args[:3] == ("gh", "pr", "create"):
                    body = Path(args[-1]).read_text()
                    self.assertIn(FLOW.MARKER, body)
                    self.assertIn("3.1.0", body)
                    return f"https://github.com/{REPO}/pull/7"
                return ""

            try:
                os.chdir(root)
                (root / FLOW.VERSION_FILE).write_text(XML)
                env = {"RUNNER_TEMP": directory, "GITHUB_STEP_SUMMARY": str(root / "summary")}
                with patch.dict(os.environ, env), patch.object(FLOW, "run", side_effect=run), patch.object(FLOW, "api", return_value=[{"filename": FLOW.VERSION_FILE}]):
                    FLOW.prepare_pr(REPO, "3.1.0", SHA, XML)
                self.assertEqual(FLOW.product_version((root / FLOW.VERSION_FILE).read_text()), "3.1.0")
                self.assertIn(("git", "push", "origin", "HEAD:refs/heads/release/v3.1.0"), commands)
                self.assertIn(("gh", "workflow", "run", "build.yml", "--repo", REPO, "--ref", "release/v3.1.0"), commands)
                self.assertFalse(any("--force" in command for command in commands))
            finally:
                os.chdir(previous)

    def test_existing_request_is_reused_without_push(self):
        with tempfile.TemporaryDirectory() as directory:
            commands = []

            def run(*args):
                commands.append(args)
                if args[:3] == ("gh", "pr", "list"):
                    return json.dumps([{"number": 7, "url": "https://example.invalid/pr/7", "body": FLOW.MARKER}])
                if args[:3] == ("git", "show", "FETCH_HEAD:Directory.Build.props"):
                    return FLOW.set_version(XML, "3.1.0")
                return ""

            def api(path):
                return [{"filename": FLOW.VERSION_FILE}] if "/files" in path else {"user": {"login": "github-actions[bot]"}}

            with patch.dict(os.environ, {"GITHUB_STEP_SUMMARY": str(Path(directory) / "summary")}), patch.object(FLOW, "run", side_effect=run), patch.object(FLOW, "api", side_effect=api):
                FLOW.prepare_pr(REPO, "3.1.0", SHA, XML)
            self.assertFalse(any(command[:2] in (("git", "push"), ("git", "commit")) for command in commands))
            self.assertTrue(any(command[:3] == ("gh", "workflow", "run") for command in commands))

    def test_direct_release_requires_green_main_ci(self):
        for result in ("failure", "cancelled", None):
            with self.subTest(result=result), self.assertRaises(ValueError):
                self.invoke(main_ci=result)

    def test_normal_main_ci_does_not_release(self):
        output, prepared = self.invoke(event_name="workflow_run")
        self.assertIn("should_release=false", output)
        self.assertEqual(prepared, 0)

    def test_merged_request_releases_exact_ci_commit(self):
        output, _ = self.invoke(event_name="workflow_run", prs=[PR])
        self.assertIn("should_release=true", output)
        self.assertIn(f"sha={SHA}", output)

    def test_unexpected_pr_changes_rejected(self):
        with self.assertRaises(ValueError):
            self.invoke(event_name="workflow_run", prs=[PR], changed_files=[FLOW.VERSION_FILE, "src/Other.cs"])

    def test_unsuccessful_or_non_main_ci_rejected(self):
        for kwargs in ({"conclusion": "failure"}, {"branch": "feature"}, {"source_event": "pull_request"}):
            with self.subTest(kwargs=kwargs), self.assertRaises(ValueError):
                self.invoke(event_name="workflow_run", prs=[PR], **kwargs)


if __name__ == "__main__":
    unittest.main()
