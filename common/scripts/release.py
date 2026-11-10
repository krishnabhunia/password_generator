#!/usr/bin/env python3
"""Versions, packaging and releases for Password Generator.

  plan      work out the version for this run (x.y.z on main, x.y.z-betaNNN on a pull request)
  features  check docs/FEATURES.md
  package   build the single ZIP with the agreed folder layout
  publish   create or update the GitHub release

Version rules (docs/RELEASING.md has the plain-language version):
  * The first release is 1.0.0.
  * After that the commit messages decide: a breaking change bumps x, a feature bumps y,
    anything else bumps z.
  * A pull request build is a beta: x.y.z-betaNNN. NNN starts at 001 for each x.y.z and
    counts up across all pull requests. Re-running the same commit keeps its number.
    The number is reserved by creating the tag on the pull request's commit.
  * A build on main is the full release x.y.z.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import os
import re
import shutil
import subprocess
import sys
import zipfile
from pathlib import Path

APP = "PasswordGenerator"
TITLE = "Password Generator"
FEATURES = "docs/FEATURES.md"
CHANGELOG = "docs/CHANGELOG.md"

STABLE = re.compile(r"^v(\d+)\.(\d+)\.(\d+)$")
BETA = re.compile(r"^v(\d+)\.(\d+)\.(\d+)-beta(\d{3})$")
TYPED = re.compile(
    r"^(?:claude\s*:\s*)?(feat|feature|fix|docs|refactor|test|chore|ci|build|perf|performance)(\([^)]*\))?(!)?\s*:",
    re.IGNORECASE,
)

Version = tuple[int, int, int]


class ReleaseError(Exception):
    pass


# ---------------------------------------------------------------- version rules

def text(version: Version) -> str:
    return ".".join(str(n) for n in version)


def bump_kind(messages: list[str]) -> str:
    """'major', 'minor' or 'patch' from commit messages and pull request titles."""
    kind = "patch"
    for message in messages:
        for raw in message.splitlines():
            line = raw.strip().lstrip("*-").strip()
            if "BREAKING CHANGE" in line:
                return "major"
            match = TYPED.match(line)
            if not match:
                continue
            if match.group(3):
                return "major"
            if match.group(1).lower() in ("feat", "feature"):
                kind = "minor"
    return kind


def next_base(stable: list[Version], kind: str) -> Version:
    if not stable:
        return (1, 0, 0)
    major, minor, patch = max(stable)
    if kind == "major":
        return (major + 1, 0, 0)
    if kind == "minor":
        return (major, minor + 1, 0)
    return (major, minor, patch + 1)


def make_plan(event_name: str, ref: str, head: str, pr_number: int | None, messages: list[str], tags: dict[str, str]) -> dict:
    """tags maps a tag name to the commit it points at."""
    stable = [tuple(int(n) for n in m.groups()) for m in map(STABLE.match, tags) if m]
    kind = bump_kind(messages)

    if event_name == "pull_request":
        base = next_base(stable, kind)
        numbers: dict[int, str] = {}
        for tag, sha in tags.items():
            m = BETA.match(tag)
            if m and tuple(int(n) for n in m.groups()[:3]) == base:
                numbers[int(m.group(4))] = sha
        mine = [n for n, sha in numbers.items() if sha == head]
        number = max(mine) if mine else max(numbers, default=0) + 1
        if number > 999:
            raise ReleaseError(f"{text(base)} has used all 999 beta numbers")
        version = f"{text(base)}-beta{number:03d}"
        return {
            "base": text(base), "version": version, "tag": "v" + version, "kind": kind, "channel": "beta",
            "publish": True, "reserve": not mine, "sha": head, "pr": pr_number,
        }

    released = [tag for tag, sha in tags.items() if sha == head and STABLE.match(tag)]
    if released:
        tag = max(released, key=lambda t: tuple(int(n) for n in STABLE.match(t).groups()))
        return {
            "base": tag[1:], "version": tag[1:], "tag": tag, "kind": kind, "channel": "stable",
            "publish": False, "reserve": False, "sha": head, "pr": None, "already_released": True,
        }

    base = next_base(stable, kind)
    if ref == "refs/heads/main":
        return {
            "base": text(base), "version": text(base), "tag": "v" + text(base), "kind": kind, "channel": "stable",
            "publish": True, "reserve": False, "sha": head, "pr": None,
        }
    # A manual run on another branch: built and tested, never published.
    version = f"{text(base)}-beta000"
    return {
        "base": text(base), "version": version, "tag": "v" + version, "kind": kind, "channel": "dev",
        "publish": False, "reserve": False, "sha": head, "pr": None,
    }


# ---------------------------------------------------------------- helpers

def run(*command: str, check: bool = True) -> str:
    result = subprocess.run(command, capture_output=True, text=True, encoding="utf-8", errors="replace")
    if check and result.returncode != 0:
        raise ReleaseError(f"{' '.join(command)} failed:\n{result.stdout}\n{result.stderr}".strip())
    return result.stdout


def read_tags() -> dict[str, str]:
    tags: dict[str, str] = {}
    out = run("git", "for-each-ref", "--format=%(refname:short)|%(objectname)|%(*objectname)", "refs/tags")
    for line in out.splitlines():
        name, sha, peeled = (line.split("|") + ["", ""])[:3]
        tags[name] = peeled or sha
    return tags


def set_outputs(values: dict) -> None:
    target = os.environ.get("GITHUB_OUTPUT")
    lines = [f"{key}={str(value).lower() if isinstance(value, bool) else value}" for key, value in values.items()]
    if target:
        with open(target, "a", encoding="utf-8") as handle:
            handle.write("\n".join(lines) + "\n")
    print("\n".join(lines))


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with open(path, "rb") as handle:
        for block in iter(lambda: handle.read(1 << 20), b""):
            digest.update(block)
    return digest.hexdigest()


def changelog_section(markdown: str, base: str) -> str:
    """The bullet lines under '## <base>' in docs/CHANGELOG.md."""
    lines: list[str] = []
    inside = False
    for line in markdown.splitlines():
        if line.startswith("## "):
            if inside:
                break
            inside = line[3:].strip().lstrip("v").split()[0:1] == [base]
            continue
        if inside and line.strip():
            lines.append(line.rstrip())
    return "\n".join(lines)


# ---------------------------------------------------------------- commands

def cmd_plan(args: argparse.Namespace) -> None:
    event = json.loads(Path(args.event).read_text(encoding="utf-8")) if args.event else {}
    tags = read_tags()
    pr_number = None
    if args.event_name == "pull_request":
        pull = event.get("pull_request", {})
        pr_number = pull.get("number")
        base_sha = pull.get("base", {}).get("sha", "")
        messages = [pull.get("title", ""), run("git", "log", "--format=%B", f"{base_sha}..{args.head}")]
    else:
        stable = sorted((t for t in tags if STABLE.match(t)), key=lambda t: tuple(int(n) for n in STABLE.match(t).groups()))
        span = f"{stable[-1]}..{args.head}" if stable else args.head
        messages = [run("git", "log", "--format=%B", span)]

    plan = make_plan(args.event_name, args.ref, args.head, pr_number, messages, tags)
    if plan["reserve"] and args.reserve:
        # Creating the tag on the pull request's commit is what reserves the beta number.
        run("gh", "api", f"repos/{args.repo}/git/refs", "-f", f"ref=refs/tags/{plan['tag']}", "-f", f"sha={args.head}")
    Path(args.out).write_text(json.dumps(plan, indent=2), encoding="utf-8")
    set_outputs({key: plan[key] for key in ("base", "version", "tag", "kind", "channel", "publish")})
    print(f"::notice title=Version::{plan['version']} ({plan['channel']}, {plan['kind']} change)")


def cmd_features(args: argparse.Namespace) -> None:
    path = Path(FEATURES)
    if not path.exists():
        raise ReleaseError(f"{FEATURES} is missing. The Info window shows it as 'What's implemented'.")
    bullets = [line for line in path.read_text(encoding="utf-8").splitlines() if line.startswith("- ")]
    if len(bullets) < 5:
        raise ReleaseError(f"{FEATURES} lists only {len(bullets)} features.")
    plan = json.loads(Path(args.plan).read_text(encoding="utf-8"))
    if plan["channel"] == "beta" and plan["kind"] in ("minor", "major") and args.base_sha:
        changed = run("git", "diff", "--name-only", f"{args.base_sha}...{args.head_sha}").splitlines()
        if FEATURES not in changed:
            raise ReleaseError(f"This pull request adds a feature but does not update {FEATURES}.")
    print(f"{FEATURES}: {len(bullets)} features listed")


def layout(version: str, dmg: bool = False, apk: bool = False) -> list[str]:
    """The exact file list inside PasswordGenerator_<version>.zip."""
    names = [f"portable/{APP}_{version}.exe", f"windows-x64/{APP}_{version}.exe"]
    if dmg:
        names.append(f"macOS/{APP}_{version}.dmg")
    if apk:
        names.append(f"Android/{APP}_{version}.apk")
    return names


def cmd_package(args: argparse.Namespace) -> None:
    version = args.version
    out = Path(args.out)
    assets = out / "assets"
    if assets.exists():
        shutil.rmtree(assets)
    assets.mkdir(parents=True)

    sources = {f"portable/{APP}_{version}.exe": Path(args.portable), f"windows-x64/{APP}_{version}.exe": Path(args.installer)}
    if args.dmg:
        sources[f"macOS/{APP}_{version}.dmg"] = Path(args.dmg)
    if args.apk:
        sources[f"Android/{APP}_{version}.apk"] = Path(args.apk)
    for name, source in sources.items():
        if not source.is_file() or source.stat().st_size == 0:
            raise ReleaseError(f"Nothing to package for {name}: {source} is missing or empty")

    archive = out / f"{APP}_{version}.zip"
    with zipfile.ZipFile(archive, "w", zipfile.ZIP_DEFLATED, compresslevel=6) as bundle:
        for name, source in sources.items():
            bundle.write(source, name)
    with zipfile.ZipFile(archive) as bundle:
        found = sorted(bundle.namelist())
    wanted = sorted(layout(version, bool(args.dmg), bool(args.apk)))
    if found != wanted:
        raise ReleaseError(f"The ZIP holds {found}, expected {wanted}")

    # Loose files for the release page. The app's updater downloads these by name.
    shutil.copyfile(args.portable, assets / f"{APP}_{version}_portable.exe")
    shutil.copyfile(args.installer, assets / f"{APP}_{version}_setup.exe")
    if args.dmg:
        shutil.copyfile(args.dmg, assets / f"{APP}_{version}.dmg")
    if args.apk:
        shutil.copyfile(args.apk, assets / f"{APP}_{version}.apk")
    shutil.copyfile(archive, assets / archive.name)
    sums = "".join(f"{sha256(p)}  {p.name}\n" for p in sorted(assets.iterdir()))
    (assets / "SHA256SUMS.txt").write_text(sums, encoding="utf-8", newline="\n")
    print(f"{archive} ({archive.stat().st_size // 1024} KB): {', '.join(found)}")


def cmd_publish(args: argparse.Namespace) -> None:
    plan = json.loads(Path(args.plan).read_text(encoding="utf-8"))
    if not plan["publish"]:
        print("Nothing to publish for this run")
        return
    tag, version, beta = plan["tag"], plan["version"], plan["channel"] == "beta"
    files = [str(p) for p in sorted((Path(args.dist) / "assets").iterdir())]
    if beta and args.screenshots and Path(args.screenshots).is_file():
        # Pictures of every design from the self-test, so a test version can be looked at before installing.
        named = Path(args.dist) / f"{APP}_{version}_screenshots.zip"
        shutil.copyfile(args.screenshots, named)
        files.append(str(named))

    if beta:
        notes = (f"Test version built from pull request #{plan['pr']} (commit {plan['sha'][:7]}).\n\n"
                 "This is not a full release. The full version is published when the pull request is merged into main.")
        title = f"{TITLE} {version} (test version)"
    else:
        section = changelog_section(Path(CHANGELOG).read_text(encoding="utf-8"), plan["base"]) if Path(CHANGELOG).exists() else ""
        notes = (section or "See docs/CHANGELOG.md.") + (
            f"\n\n**Download:** `{APP}_{version}.zip` holds the portable and the installed version.")
        title = f"{TITLE} {version}"
    notes_file = Path(args.dist) / "release-notes.md"
    notes_file.write_text(notes, encoding="utf-8")

    exists = subprocess.run(["gh", "release", "view", tag], capture_output=True, text=True).returncode == 0
    if exists:
        run("gh", "release", "upload", tag, *files, "--clobber")
    else:
        flags = ["--prerelease", "--latest=false"] if beta else ["--latest"]
        run("gh", "release", "create", tag, *files, "--target", plan["sha"], "--title", title, "--notes-file", str(notes_file), *flags)
    print(f"::notice title=Released::{tag} with {len(files)} files")


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = parser.add_subparsers(dest="command", required=True)

    p = sub.add_parser("plan")
    p.add_argument("--event-name", required=True)
    p.add_argument("--event", default="")
    p.add_argument("--ref", default="")
    p.add_argument("--head", required=True)
    p.add_argument("--repo", default="")
    p.add_argument("--reserve", action="store_true")
    p.add_argument("--out", default="release-plan.json")
    p.set_defaults(run=cmd_plan)

    p = sub.add_parser("features")
    p.add_argument("--plan", required=True)
    p.add_argument("--base-sha", default="")
    p.add_argument("--head-sha", default="")
    p.set_defaults(run=cmd_features)

    p = sub.add_parser("package")
    p.add_argument("--version", required=True)
    p.add_argument("--portable", required=True)
    p.add_argument("--installer", required=True)
    p.add_argument("--dmg", default="")
    p.add_argument("--apk", default="")
    p.add_argument("--out", default="dist")
    p.set_defaults(run=cmd_package)

    p = sub.add_parser("publish")
    p.add_argument("--plan", required=True)
    p.add_argument("--dist", default="dist")
    p.add_argument("--screenshots", default="")
    p.set_defaults(run=cmd_publish)

    args = parser.parse_args(argv)
    try:
        args.run(args)
    except ReleaseError as error:
        message = str(error).replace("%", "%25").replace("\r", "").replace("\n", "%0A")
        print(f"::error title=release.py {args.command}::{message}")
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
