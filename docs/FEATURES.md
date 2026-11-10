# What's implemented

## Making a password
- Switch each element on or off: uppercase A–Z, lowercase a–z, digits 0–9 and special characters.
- Special characters are separate: each of the 28 symbols has its own chip, with Select all and Clear.
- Password length from 4 to 64, with a slider and − / + buttons.
- A minimum count for each element (0 to 9). The length grows by itself when the minimums need more room.
- A new password appears as soon as any choice changes. Regenerate makes another one with the same choices.
- Random numbers come from the operating system's cryptographic generator.

## Reading and using it
- Letters, digits and special characters are shown in different colours.
- A strength meter with a label (Weak, Fair, Strong, Very strong) and the figure in bits. It counts the passwords your choices really allow, minimums included.
- Every password that fits your choices is equally likely.
- Copy puts the password on the clipboard.
- Generated passwords are never saved to disk, and no password is ever sent anywhere.

## Changing a password
- A Change a password window opens from the "Change a password…" button, the key icon at the bottom, or Ctrl+K.
- Paste the password you use now. It is hidden by default, with Show, Paste and Forget it.
- That current password is kept after a restart, stored on this PC only and encrypted with your Windows sign-in.
- The new password is the one from the generator; New makes another.
- A typing order you build yourself from Current, New, Tab, Enter and Wait 1 s. Add, remove and move the steps; the order is remembered.
- "Type it into that window" switches to the window you used last and types the order into it at once, as KeePass does.
- The window that will receive the typing is named before you press the button.
- Typing stops by itself if another window comes to the front, and Esc stops it too.
- Afterwards "Did the change work?" Yes makes the new password the current one and generates a fresh new one.

## Designs
- Two-pane Light (the default): choices on the left, password on the right.
- Two-pane Dark: the same layout in dark colours.
- Fluent Light: one column of cards.
- Compact: a small window with element tiles.
- The design is chosen in Settings and changes straight away.

## Settings and updates
- Your choices and the design are remembered between runs.
- The app checks GitHub for a newer version at every start.
- An "Update to vx.y.z" button appears at the top right only when an update is found.
- Settings has "Check for updates now", "Install updates automatically in the background" and "Also offer beta (test) versions".
- Every download is checked against its SHA-256 before it is installed.

## Info
- The version number is shown at the bottom of the main window.
- The Info button, F1 and Settings open the Info window with four tabs: This build, What's implemented, What's new and Under the hood.
- "Copy build info" copies the build details.

## Windows packages
- A portable version (one file, nothing to install) and an installed version (set-up program, Start menu entry, uninstaller).
