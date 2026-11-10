# macOS

Not built yet. The desktop app in `common/src/PasswordGenerator.App` uses Avalonia, which also runs on macOS, so a Mac version would add its packaging here (app bundle and `.dmg`) and a build step in `.github/workflows/build.yml`. The release ZIP already has a place for it: `macOS/PasswordGenerator_<version>.dmg`.
