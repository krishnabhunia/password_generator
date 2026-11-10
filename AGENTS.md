# Notes for anyone (person or AI) changing this repository

- Layout: `docs/`, `windows/`, `macOS/`, `android/` and `common/` (with `src/`, `tests/`, `scripts/`). Shared code goes in `common/`, platform-only code in its platform folder.
- Stack: C#, .NET 10, Avalonia 12. The generator and everything testable without a window is in `PasswordGenerator.Core`.
- Versions are worked out by `common/scripts/release.py` from commit messages. Use Conventional Commits (`feat:`, `fix:`, …). Never hand-edit a version.
- A change that adds a feature must update `docs/FEATURES.md` and `docs/CHANGELOG.md`; the pipeline fails otherwise. Keep those two files to headings (`#`, `##`) and bullets (`- `): the Info window shows them as they are.
- After a UI change, run the self-test and look at the pictures:
  `dotnet run --project common/src/PasswordGenerator.App -- --smoke-test <folder> --data-dir <folder>`
  It fails when text or a control is cut off in any design.
- Passwords must never be written to disk, logged or sent anywhere.
