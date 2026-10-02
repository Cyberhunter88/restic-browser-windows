"""Exercise archive/error handling with fixtures; real GUI tests run separately in CI."""

import json
import os
from pathlib import Path
import subprocess
import tarfile
import tempfile
import unittest


ROOT = Path(__file__).resolve().parents[2]
EXPECTED_VERSION = json.loads((ROOT / "build/restic-manifest.json").read_text())["version"]


class LinuxPackageTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix="restic package tests ")
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.package = self.root / "package"
        (self.package / "tools").mkdir(parents=True)
        (self.package / "README.md").write_text("Fixture")
        (self.package / "LICENSE").write_text("Fixture")
        self.executable(self.package / "ResticBrowser", """#!/usr/bin/env bash
set -eu
test "$PWD" != "$(dirname -- "$0")"
case "$0" in *'Portable Restic Browser/'*) ;; *) exit 1 ;; esac
test -d "$HOME"
test "$XDG_DATA_HOME" = "$HOME/data"
sleep 30
""")
        self.restic({"version": EXPECTED_VERSION, "go_os": "linux", "go_arch": "amd64"})
        self.bin = self.root / "bin"
        self.bin.mkdir()
        # Only simulate the display wrapper; use real timeout and processes.
        self.executable(self.bin / "xvfb-run", """#!/usr/bin/env bash
set -eu
test "$1" = '--auto-servernum'
test "$2" = '--server-args=-screen 0 1280x720x24'
shift 2
exec "$@"
""")

    @staticmethod
    def executable(path, content):
        path.write_text(content)
        path.chmod(0o755)

    def restic(self, message):
        self.executable(self.package / "tools/restic", "#!/usr/bin/env bash\n"
                        "test \"$*\" = 'version --json' || exit 1\n"
                        "printf '%s\\n' '" + json.dumps(message) + "'\n")

    def verify(self):
        archive = self.root / "portable package.tar.gz"
        with tarfile.open(archive, "w:gz") as output:
            for entry in self.package.iterdir():
                output.add(entry, arcname=entry.name)
        env = os.environ.copy()
        env["PATH"] = str(self.bin) + os.pathsep + env["PATH"]
        env["TMPDIR"] = str(self.root)
        result = subprocess.run(["bash", str(ROOT / "scripts/verify-linux-package.sh"), str(archive)],
                                cwd="/", env=env, text=True, capture_output=True, timeout=25)
        self.assertEqual(list(self.root.glob("tmp.*")), [], "Verifier left temporary files behind")
        return result

    def test_portable_paths_and_expected_timeout(self):
        result = self.verify()
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)

    def test_missing_restic(self):
        (self.package / "tools/restic").unlink()
        self.assertNotEqual(self.verify().returncode, 0)

    def test_missing_readme(self):
        (self.package / "README.md").unlink()
        self.assertNotEqual(self.verify().returncode, 0)

    def test_non_executable_application(self):
        (self.package / "ResticBrowser").chmod(0o644)
        self.assertNotEqual(self.verify().returncode, 0)

    def test_wrong_restic_version(self):
        self.restic({"version": "0.0.0", "go_os": "linux", "go_arch": "amd64"})
        self.assertNotEqual(self.verify().returncode, 0)

    def test_invalid_restic_json(self):
        self.executable(self.package / "tools/restic", "#!/bin/sh\necho invalid\n")
        self.assertNotEqual(self.verify().returncode, 0)

    def test_wrong_restic_architecture(self):
        self.restic({"version": EXPECTED_VERSION, "go_os": "linux", "go_arch": "arm64"})
        self.assertNotEqual(self.verify().returncode, 0)

    def test_early_gui_exit_is_failure(self):
        self.executable(self.package / "ResticBrowser", "#!/bin/sh\nexit 0\n")
        result = self.verify()
        self.assertNotEqual(result.returncode, 0)
        self.assertIn("Exitcode 0", result.stderr)


if __name__ == "__main__":
    unittest.main()
