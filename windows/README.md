# Windows

The app itself is shared desktop code in `common/src/PasswordGenerator.App`. This folder holds what only Windows needs.

| File | What it is |
|---|---|
| `installer/PasswordGenerator.iss` | Inno Setup script for the set-up program. Installs for the current user (no administrator rights), adds a Start menu entry and an uninstaller. |

The pipeline builds both Windows packages: the portable single file and the set-up program.
