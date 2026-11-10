# Notes for anyone (person or AI) changing this repository

- Layout: `docs/`, `windows/`, `macOS/`, `android/` and `common/` (with `src/`, `tests/`, `scripts/`). Shared code goes in `common/`, platform-only code in its platform folder.
- Stack: C#, .NET 10, Avalonia 12. The generator and everything testable without a window is in `PasswordGenerator.Core`.
- Versions are worked out by `common/scripts/release.py` from commit messages. Use Conventional Commits (`feat:`, `fix:`, …). Never hand-edit a version.
- A change that adds a feature must update `docs/FEATURES.md` and `docs/CHANGELOG.md`; the pipeline fails otherwise. Keep those two files to headings (`#`, `##`) and bullets (`- `): the Info window shows them as they are.
- After a UI change, run the self-test and look at the pictures:
  `dotnet run --project common/src/PasswordGenerator.App -- --smoke-test <folder> --data-dir <folder>`
  It fails when text or a control is cut off in any design.
- Passwords must never be logged or sent anywhere, and generated passwords must never be written to disk. The only stored password is the current one from the Change a password window, and only through `SecretFile` with Windows DPAPI (`DpapiProtector`); without a protector nothing is written.
- The self-test saves pictures, so it runs on `SampleRandomSource` (a fixed sequence) and shows sample passwords only. Its typing check types sample text into its own `TypingProbeWindow`, never into another program.
- Typing into other windows (`WindowsTyper`) must keep its safety stops: it checks before every character that the chosen window is still in front, and stops on Esc.
- The Change a password window types only into the window named on screen. Typing changes nothing in the app: the current password changes only when the user changes it (Paste, typing in the box, Forget it), and the new password stays until New or Regenerate. There is no question after typing (Krishna's decision, 10 Nov 2026). The self-test's "Change rules" cover these; keep them passing.
- The self-test always runs on its own data folder (`<smoke folder>/data` unless `--data-dir` names another), never on the user's.
