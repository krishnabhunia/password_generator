# Designs

Krishna approved the designs on 10 Nov 2026 and chose Two-pane Light as the default. All four are in the app; Settings → Design switches between them.

| Design | Window | Layout | Colours |
|---|---|---|---|
| Two-pane Light (default) | 980 wide | Choices on the left, password on the right | Light, blue accent |
| Two-pane Dark | 980 wide | The same layout | Dark, amber accent |
| Fluent Light | 560 wide | One column of cards | Light, blue accent |
| Compact | 420 wide | Small window, four element tiles | Light, green accent |

The window is as tall as its content needs and cannot be resized.

## Where they live in the code

| Part | File |
|---|---|
| Layouts | `common/src/PasswordGenerator.App/Views/TwoPaneView.axaml`, `FluentView.axaml`, `CompactView.axaml` |
| Colours | `common/src/PasswordGenerator.App/Palettes/Light.axaml`, `Dark.axaml`, `Compact.axaml` |
| The list of designs | `common/src/PasswordGenerator.App/Services/DesignInfo.cs` |
| Shared control styles | `common/src/PasswordGenerator.App/Styles/Controls.axaml` |

Adding a design means adding a layout and/or a palette and one line in `DesignInfo.All`. The self-test (`--smoke-test <folder>`) then opens it automatically and saves pictures of it.

## Change a password window (design CP1, option B)

Approved by Krishna on 10 Nov 2026: the two-pane option, typing "as in KeePass" (switch to the window used last and type at once), the current password stored encrypted with the Windows sign-in, opened from a button under Copy and Regenerate, a key icon in the footer and Ctrl+K.

| Part | File |
|---|---|
| Window | `common/src/PasswordGenerator.App/Views/ChangePasswordWindow.axaml` |
| Behaviour | `common/src/PasswordGenerator.App/ViewModels/ChangePasswordViewModel.cs` |
| Typing order and plan | `common/src/PasswordGenerator.Core/Typing.cs` |
| Encrypted store | `common/src/PasswordGenerator.Core/SecretFile.cs`, `common/src/PasswordGenerator.App/Services/DpapiProtector.cs` |
| Typing into another window | `common/src/PasswordGenerator.App/Services/KeyboardTyper.cs` |

The window uses the colours of the design chosen in Settings. It grows taller when the typing order needs more than two rows.
