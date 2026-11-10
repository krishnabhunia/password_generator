# Versions and releases

Everything here is automatic. Nobody edits a version number by hand.

## Version numbers

Versions are `x.y.z`.

| Part | Goes up when | Decided by |
|---|---|---|
| `x` major | Something changes in a way that breaks what existed | A commit marked `!` (for example `feat!:`) or containing `BREAKING CHANGE` |
| `y` minor | A feature is added | A `feat:` commit, or a pull request titled `Claude : Feature: …` |
| `z` patch | A bug or error is fixed, or anything else changes | Every other commit |

The first release is `1.0.0`.

## Beta and full versions

| Where | Version | What happens |
|---|---|---|
| Pull request | `x.y.z-betaNNN`, for example `1.1.0-beta003` | Published as a pre-release on the Releases page |
| `main` | `x.y.z` | Published as the full release when the pull request is merged |

- `NNN` starts at `001` for each new `x.y.z` and counts up across all pull requests on that version.
- Each new push to a pull request gets the next number. Re-running the same commit keeps its number.
- The number is reserved by creating the tag on the pull request's commit, so a failed build's number is skipped, never reused.
- The app ranks `x.y.z-betaNNN` below `x.y.z`. Beta versions are only offered to people who switched on "Also offer beta (test) versions".

## The pipeline

One workflow, `.github/workflows/build.yml`, does all of this on every pull request and on every push to `main`:

1. Tests the release scripts.
2. Works out the version.
3. Checks `docs/FEATURES.md` (a pull request that adds a feature must update it).
4. Runs the unit tests.
5. Builds the portable app and checks the version inside it.
6. Starts the app in every design and fails if anything is cut off.
7. Builds the set-up program, installs it, checks it and uninstalls it.
8. Packs one ZIP and uploads it as the run's only artifact.
9. Publishes the beta or the full release.

## What a run produces

The Artifacts box of a run holds exactly one file, `PasswordGenerator_<version>.zip`:

```
portable/PasswordGenerator_<version>.exe
windows-x64/PasswordGenerator_<version>.exe
macOS/PasswordGenerator_<version>.dmg      (when a Mac version exists)
Android/PasswordGenerator_<version>.apk    (when an Android version exists)
```

The release page also carries the two programs on their own (`…_portable.exe`, `…_setup.exe`) and `SHA256SUMS.txt`. The app's updater downloads those and checks the SHA-256 before installing. Beta releases also carry `…_screenshots.zip`, pictures of every design taken during the self-test.
