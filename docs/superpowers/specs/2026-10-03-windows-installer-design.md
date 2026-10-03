# CodexLauncher Windows Installer Design

**Status:** Draft for user review  
**Date:** 2026-10-03

## Goal

Replace the duplicate self-contained ZIP with a real Windows x64 setup executable. Keep the existing portable ZIP as the only archive distribution. The installer must carry the .NET runtime so a target PC can install and run CodexLauncher without installing .NET separately.

## Current behavior

- `CodexLauncher-v1.0.3-win-x64-portable.zip` contains a `CodexLauncher.portable` marker. The app stores settings, logs, and bridge state under `portable-data` beside the app.
- The unmarked build stores user data under `%LOCALAPPDATA%\CodexLauncher`.
- The self-contained ZIP and portable ZIP contain essentially the same program/runtime; the marker selects the data location.
- The repository has no Windows installer project or installer script.

## Selected approach

Use Inno Setup to build one `CodexLauncher-v1.0.3-win-x64-setup.exe` from the existing self-contained `win-x64` publish output. Use per-user install mode with the default destination `%LOCALAPPDATA%\Programs\CodexLauncher`, so installation does not require administrator rights. Keep the portable build and its marker unchanged.

The wizard creates a Start Menu shortcut and offers a desktop shortcut. The installed app continues to use `%LOCALAPPDATA%\CodexLauncher` for settings, logs, and bridge state. Upgrades replace application files without moving user data. Uninstall removes the application files and shortcuts, but deliberately leaves user data intact.

The setup contains all files from the self-contained publish output required by the app, including native dependencies and third-party notices. It does not download or install .NET as a prerequisite. No code-signing identity is available, so signing is outside this change; Windows may show its normal publisher/SmartScreen prompt for an unsigned installer.

## Alternatives considered

- **Keep one self-contained ZIP:** smallest engineering change, but it is still not a Windows installation experience and leaves no shortcut or uninstaller.
- **WiX MSI / Burn bundle:** native Windows Installer support, but more build and upgrade machinery than this single-user utility needs.
- **MSIX:** modern package management, but introduces package identity and signing/trust requirements for distribution outside the Microsoft Store.

Inno Setup is selected for its single-EXE distribution, user-level installation mode, shortcuts, and built-in uninstaller. The official license allows use for any purpose subject to retaining required notices; its website requests that commercial users purchase a license.

## Release changes

- Keep `CodexLauncher-v1.0.3-win-x64-portable.zip` and its SHA-256 file.
- Replace `CodexLauncher-v1.0.3-win-x64-self-contained.zip` and its checksum with `CodexLauncher-v1.0.3-win-x64-setup.exe` and a matching SHA-256 file after the setup has been built and verified.
- Update README build and download instructions to distinguish the setup installer from the portable archive.

## Acceptance criteria

1. The setup compiler produces one Windows x64 installer executable from a self-contained publish, with no portable marker.
2. The installer succeeds in per-user mode without an administrator prompt and installs the app plus required runtime/native files.
3. Start Menu shortcut works; desktop shortcut is optional; Windows uninstall registration is present.
4. Installed app uses `%LOCALAPPDATA%\CodexLauncher`; upgrading preserves existing settings; uninstall leaves user data intact.
5. The portable ZIP is unchanged and continues to store its data in `portable-data`.
6. Installer and portable asset hashes match their published checksum files. The v1.0.3 Release no longer contains the duplicate self-contained ZIP after the installer is ready.

## Risks and boundaries

- The installer compiler becomes a build prerequisite and must be documented with its official license/notice.
- An unsigned setup may trigger a SmartScreen or publisher warning; this work does not acquire or use a signing certificate.
- User data must not be included in the installer or removed by uninstall/upgrade actions.
- Do not clean other historical `dist` output folders as part of packaging.
