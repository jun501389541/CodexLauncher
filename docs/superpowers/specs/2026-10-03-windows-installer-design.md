# CodexLauncher Windows Installer Design

**Status:** Approved for implementation
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

The wizard creates a Start Menu shortcut and offers a desktop shortcut. The installed app stores settings, logs, proxy recovery state, and bridge state in an app-owned data directory under the install directory. The installer uses an installed-mode marker recognized by `LauncherDataPaths`; the existing portable marker and portable ZIP behavior remain unchanged. On first launch, the installed app imports existing settings and bridge state from `%LOCALAPPDATA%\CodexLauncher` using the current non-overwriting migration behavior. The original data is retained during use and upgrades. Uninstall removes the install directory and its app-local data, the app-specific legacy `%LOCALAPPDATA%\CodexLauncher` directory, Start Menu/Desktop shortcuts, and the app's `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\CodexLauncher` value. This intentionally deletes CodexLauncher settings and bridge credentials when the user uninstalls; upgrade does not delete them.

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
4. Installed app keeps all app-owned files and user data under the install directory; first launch imports supported legacy settings and bridge state without overwriting destination files; upgrading preserves app-local data.
5. Uninstall removes the install directory and its data, the app-specific legacy `%LOCALAPPDATA%\CodexLauncher` directory, shortcuts, and the app-owned Run value, leaving no CodexLauncher data behind from the installed build.
6. The portable ZIP is unchanged and continues to store its data in `portable-data`.
7. Installer and portable asset hashes match their published checksum files. The v1.0.3 Release no longer contains the duplicate self-contained ZIP after the installer is ready.

## Risks and boundaries

- The installer compiler becomes a build prerequisite and must be documented with its official license/notice.
- An unsigned setup may trigger a SmartScreen or publisher warning; this work does not acquire or use a signing certificate.
- User data must not be included in the installer or removed by upgrade actions. Uninstall deliberately removes app-local and app-specific legacy data, so the installer and README must clearly warn that uninstall permanently deletes settings and bridge credentials.
- The legacy AppData directory is shared by prior unmarked builds. Cleanup must target only `%LOCALAPPDATA%\CodexLauncher`, and must not run during install or upgrade; it runs only as part of uninstall.
- Do not clean other historical `dist` output folders as part of packaging.
