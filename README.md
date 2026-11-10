# Password Generator

A small Windows app that makes strong random passwords. You choose what goes in, it does the rest.

## What it does

- Switch each element on or off: uppercase `A–Z`, lowercase `a–z`, digits `0–9`, special characters.
- Special characters are separate: each of the 28 symbols has its own chip.
- Password length from 4 to 64, and a minimum count for each element.
- Copy, Regenerate, a strength meter, and letters / digits / symbols in different colours.
- Four designs, chosen in Settings: **Two-pane Light** (default), Two-pane Dark, Fluent Light, Compact.
- **Change a password** window: types your current password and the new one into the website or app you used last, in an order you choose, the way KeePass auto-type does.
- Generated passwords are never saved. The current password you paste for a change is kept on this PC only, encrypted with your Windows sign-in. Nothing is ever sent anywhere.

The full list is in [docs/FEATURES.md](docs/FEATURES.md).

## Download

Go to [Releases](https://github.com/krishnabhunia/password_generator/releases) and download `PasswordGenerator_<version>.zip`. Inside:

| Folder | File | Use it when |
|---|---|---|
| `portable/` | `PasswordGenerator_<version>.exe` | You want one file to run, with nothing installed |
| `windows-x64/` | `PasswordGenerator_<version>.exe` | You want it installed, with a Start menu entry and an uninstaller |

The files are not code-signed, so Windows SmartScreen may ask before the first run ("More info" → "Run anyway").

## Updates

The app checks this repository's Releases at every start. When a newer version exists, an **Update to vx.y.z** button appears at the top right. Settings also has "Check for updates now", automatic background updates, and an option to receive beta versions.

## Folders

| Folder | What is in it |
|---|---|
| `common/src/PasswordGenerator.Core` | The generator, strength estimate, versions, update feed, settings |
| `common/src/PasswordGenerator.App` | The desktop app (C#, .NET 10, Avalonia) |
| `common/tests` | Unit tests for the core |
| `common/scripts` | Version, packaging and release scripts used by the pipeline |
| `windows/` | The Windows set-up program (Inno Setup) |
| `macOS/`, `android/` | Reserved; not built yet |
| `docs/` | Features, changelog, designs, how releases work |

## Build it yourself

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```
dotnet test common/tests/PasswordGenerator.Core.Tests
dotnet run --project common/src/PasswordGenerator.App
dotnet publish common/src/PasswordGenerator.App -c Release -r win-x64 -o out/portable
```

How versions and releases work: [docs/RELEASING.md](docs/RELEASING.md).
