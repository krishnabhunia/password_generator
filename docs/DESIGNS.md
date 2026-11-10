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
