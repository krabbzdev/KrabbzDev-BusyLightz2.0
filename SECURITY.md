# Security

## Reporting a problem

Please don't open a public issue for a security problem. Use **Security > Report a vulnerability** on this repository's GitHub page, which sends it to the maintainer privately. Include what you found, the version (shown at the bottom of the window) and the steps to repeat it.

## Supported versions

Only the newest release gets fixes. The app updates itself, and **Check and install updates** in the tray menu gets the latest.

## How the app protects you

- **Updates are checked before they install.** The app downloads from this project's [Releases](https://github.com/krabbzdev/KrabbzDev-BusyLightz2.0/releases) page and compares the file with the SHA-256 fingerprint GitHub recorded for it, and refuses anything that doesn't match.
- **Your Microsoft sign-in** is stored encrypted for your Windows account only, and uses a browser sign-in; the app never sees your password.
- **Least permission:** it asks Microsoft for your own presence, and only if you allow it, your own calendar and (optionally) your meeting transcripts. See [Privacy](PRIVACY.md).
- **Installed for everyone:** the background updater runs as SYSTEM, from a folder only administrators can write to.

## Known limits

- **The installers aren't code-signed yet**, so Windows may say "unknown publisher". The SHA-256 check above is what protects automatic updates.
- **Releases aren't reviewed by a second person.** The project has one maintainer, and the Windows build is made by GitHub Actions from the `main` branch.
