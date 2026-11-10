import sys
import tempfile
import unittest
import zipfile
from pathlib import Path
from types import SimpleNamespace

sys.path.insert(0, str(Path(__file__).parent))
import release  # noqa: E402

HEAD = "a" * 40
OTHER = "b" * 40


class BumpKind(unittest.TestCase):
    def test_feature_is_minor(self):
        self.assertEqual(release.bump_kind(["feat(windows): add a thing"]), "minor")
        self.assertEqual(release.bump_kind(["Claude : Feature: start-up screen (SP1 B) — 14.0.0"]), "minor")

    def test_fix_and_others_are_patch(self):
        self.assertEqual(release.bump_kind(["fix: typo", "docs: readme", "ci(repo): cache"]), "patch")
        self.assertEqual(release.bump_kind(["Claude : Fix: keep update checks running (#149) — 18.2.1"]), "patch")
        self.assertEqual(release.bump_kind(["no type at all"]), "patch")

    def test_breaking_is_major(self):
        self.assertEqual(release.bump_kind(["feat!: new settings file"]), "major")
        self.assertEqual(release.bump_kind(["fix: x\n\nBREAKING CHANGE: settings reset"]), "major")

    def test_squash_message_lists_are_read(self):
        self.assertEqual(release.bump_kind(["Merge pull request #3\n\n* fix: one\n* feat(common): two"]), "minor")


class Plan(unittest.TestCase):
    def test_first_pull_request_is_1_0_0_beta001(self):
        plan = release.make_plan("pull_request", "refs/pull/2/merge", HEAD, 2, ["feat: app"], {})
        self.assertEqual((plan["version"], plan["tag"], plan["channel"]), ("1.0.0-beta001", "v1.0.0-beta001", "beta"))
        self.assertTrue(plan["publish"])
        self.assertTrue(plan["reserve"])
        self.assertEqual(plan["pr"], 2)

    def test_new_push_gets_the_next_number(self):
        tags = {"v1.0.0-beta001": OTHER, "v1.0.0-beta002": OTHER}
        plan = release.make_plan("pull_request", "", HEAD, 2, ["feat: app"], tags)
        self.assertEqual(plan["version"], "1.0.0-beta003")

    def test_same_commit_keeps_its_number(self):
        tags = {"v1.0.0-beta001": OTHER, "v1.0.0-beta002": HEAD, "v1.0.0-beta003": OTHER}
        plan = release.make_plan("pull_request", "", HEAD, 2, ["feat: app"], tags)
        self.assertEqual(plan["version"], "1.0.0-beta002")
        self.assertFalse(plan["reserve"])

    def test_numbers_restart_for_each_version(self):
        tags = {"v1.0.0": OTHER, "v1.0.0-beta009": OTHER}
        self.assertEqual(release.make_plan("pull_request", "", HEAD, 5, ["fix: x"], tags)["version"], "1.0.1-beta001")
        self.assertEqual(release.make_plan("pull_request", "", HEAD, 5, ["feat: x"], tags)["version"], "1.1.0-beta001")
        self.assertEqual(release.make_plan("pull_request", "", HEAD, 5, ["feat!: x"], tags)["version"], "2.0.0-beta001")

    def test_all_numbers_used(self):
        tags = {"v1.0.0-beta999": OTHER}
        with self.assertRaises(release.ReleaseError):
            release.make_plan("pull_request", "", HEAD, 2, ["feat: app"], tags)

    def test_main_publishes_the_full_version(self):
        plan = release.make_plan("push", "refs/heads/main", HEAD, None, ["feat: app"], {"v1.0.0-beta004": OTHER})
        self.assertEqual((plan["version"], plan["tag"], plan["channel"], plan["publish"]), ("1.0.0", "v1.0.0", "stable", True))

    def test_main_after_a_release(self):
        tags = {"v1.0.0": OTHER, "v1.1.0": OTHER}
        self.assertEqual(release.make_plan("push", "refs/heads/main", HEAD, None, ["fix: x"], tags)["version"], "1.1.1")
        self.assertEqual(release.make_plan("push", "refs/heads/main", HEAD, None, ["feat: x"], tags)["version"], "1.2.0")

    def test_released_commit_is_not_released_again(self):
        plan = release.make_plan("push", "refs/heads/main", HEAD, None, ["feat: x"], {"v1.0.0": HEAD})
        self.assertEqual(plan["version"], "1.0.0")
        self.assertFalse(plan["publish"])

    def test_manual_run_on_a_branch_is_never_published(self):
        plan = release.make_plan("workflow_dispatch", "refs/heads/some-branch", HEAD, None, ["feat: x"], {})
        self.assertFalse(plan["publish"])
        self.assertEqual(plan["version"], "1.0.0-beta000")


class Package(unittest.TestCase):
    def test_zip_has_exactly_the_agreed_layout(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            (root / "p.exe").write_bytes(b"portable")
            (root / "s.exe").write_bytes(b"setup")
            args = SimpleNamespace(version="1.2.3-beta004", portable=str(root / "p.exe"), installer=str(root / "s.exe"),
                                   dmg="", apk="", out=str(root / "dist"))
            release.cmd_package(args)
            with zipfile.ZipFile(root / "dist" / "PasswordGenerator_1.2.3-beta004.zip") as bundle:
                self.assertEqual(sorted(bundle.namelist()), [
                    "portable/PasswordGenerator_1.2.3-beta004.exe",
                    "windows-x64/PasswordGenerator_1.2.3-beta004.exe",
                ])
                self.assertEqual(bundle.read("portable/PasswordGenerator_1.2.3-beta004.exe"), b"portable")
            assets = sorted(p.name for p in (root / "dist" / "assets").iterdir())
            self.assertEqual(assets, [
                "PasswordGenerator_1.2.3-beta004.zip",
                "PasswordGenerator_1.2.3-beta004_portable.exe",
                "PasswordGenerator_1.2.3-beta004_setup.exe",
                "SHA256SUMS.txt",
            ])
            sums = (root / "dist" / "assets" / "SHA256SUMS.txt").read_text()
            self.assertIn(release.sha256(root / "p.exe") + "  PasswordGenerator_1.2.3-beta004_portable.exe", sums)

    def test_mac_and_android_folders_when_present(self):
        self.assertEqual(release.layout("2.0.0", dmg=True, apk=True), [
            "portable/PasswordGenerator_2.0.0.exe",
            "windows-x64/PasswordGenerator_2.0.0.exe",
            "macOS/PasswordGenerator_2.0.0.dmg",
            "Android/PasswordGenerator_2.0.0.apk",
        ])

    def test_missing_file_stops_the_package(self):
        with tempfile.TemporaryDirectory() as temp:
            args = SimpleNamespace(version="1.0.0", portable=str(Path(temp) / "none.exe"), installer=str(Path(temp) / "none2.exe"),
                                   dmg="", apk="", out=str(Path(temp) / "dist"))
            with self.assertRaises(release.ReleaseError):
                release.cmd_package(args)


class Changelog(unittest.TestCase):
    def test_section_for_a_version(self):
        doc = "# What's new\n\n## 1.1.0\n- second\n\n## 1.0.0\n- first\n- more\n"
        self.assertEqual(release.changelog_section(doc, "1.0.0"), "- first\n- more")
        self.assertEqual(release.changelog_section(doc, "1.1.0"), "- second")
        self.assertEqual(release.changelog_section(doc, "9.9.9"), "")


if __name__ == "__main__":
    unittest.main()
