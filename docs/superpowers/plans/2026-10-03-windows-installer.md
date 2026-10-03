# Windows Installer Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Publish a per-user self-contained Windows installer for CodexLauncher that keeps installed app data inside its install directory and removes app-owned data on uninstall, while preserving the existing portable ZIP.

**Architecture:** Add an installer-specific marker interpreted by `LauncherDataPaths` so installed and portable builds both use app-local data without conflating their modes. Use Inno Setup to install the self-contained `win-x64` publish output, register shortcuts/uninstall metadata, and remove the install data, the app-specific legacy AppData folder, and the app-owned Run value on uninstall. Build and verify the installer before replacing only the duplicate self-contained asset in the existing v1.0.3 Release.

**Tech Stack:** .NET 10 WinForms, C# tests in `CodexLauncher.Tests`, Inno Setup 6.7.3, PowerShell packaging/checksum commands, GitHub Release UI in authenticated Chrome.

**Spec:** `docs/superpowers/specs/2026-10-03-windows-installer-design.md`

## Global Constraints

- Use per-user install mode; default destination is `%LOCALAPPDATA%\Programs\CodexLauncher` and installation does not require administrator rights.
- Bundle the .NET runtime; target computers do not need a separate .NET installation.
- Installed user data is under the install directory; first launch imports supported files from `%LOCALAPPDATA%\CodexLauncher` without overwriting destination files.
- Upgrades preserve app-local data; uninstall removes app-local data, only the app-specific legacy `%LOCALAPPDATA%\CodexLauncher` folder, shortcuts, and the `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\CodexLauncher` value.
- Keep portable marker behavior and the existing portable ZIP unchanged.
- Do not clean unrelated historical `dist` folders.
- Do not replace or delete the public duplicate self-contained release asset until the installer is built and verified.

## Review Focus

- Installed marker absent/present alongside portable marker: resolve to the right data path and mode without changing the ZIP's behavior.
- Legacy settings/bridge source absent, partial, or colliding with destination files: import only supported missing files and do not overwrite installed data.
- Install or upgrade cannot write to the selected directory: fail with an accurate install-path message and preserve source data.
- Uninstall removes only CodexLauncher-owned data and startup registration, including the legacy app folder, while upgrade does not run uninstall cleanup.
- Release update contains the setup and checksum, retains portable ZIP and source archives, and removes only the duplicate self-contained ZIP and checksum.

---

### Task 1: Add an installer-specific data mode

**Files:**
- Modify: `CodexLauncher.Core/LauncherDataPaths.cs`
- Modify: `CodexLauncher.Core/CodexLauncher.Core.csproj`
- Modify: `CodexLauncher.App/Program.cs`
- Create: `CodexLauncher.Tests/LauncherDataPathsTests.cs`

**Interfaces:**
- Keep the existing `LauncherDataPaths.DataDirectory`, `SettingsPath`, `BridgeDirectory`, `IsPortable`, and `InitializeForCurrentProcess()` consumers compatible.
- Add an installer marker constant named `InstalledMarkerFileName` with value `CodexLauncher.installed`; installed and portable markers both select `<exe directory>\portable-data`, but only the portable marker sets `IsPortable`.
- Keep path resolution and migration helpers internal and expose them to `CodexLauncher.Tests` through `InternalsVisibleTo`, avoiding a new public production API solely for tests.

- [x] Write a failing test that a temporary executable directory with `CodexLauncher.installed` selects its sibling `portable-data` while remaining non-portable.
- [x] Run the focused test and confirm it fails because installed mode is not recognized.
- [x] Add a test that a portable marker still selects `portable-data` and reports portable mode, and an unmarked directory still selects the supplied legacy AppData path.
- [x] Refactor path resolution behind a testable directory-based resolver; keep the current static public properties backed by the resolved current-process paths.
- [x] Update initialization to import legacy settings and bridge files for both local-data modes, preserve non-overwrite behavior, and report an accurate install-directory write failure from `Program.Main`.
- [x] Add migration tests for missing source, partial bridge files, destination collisions, and retry after a copy failure; verify the migration marker is written only after a successful migration.
- [x] Add a failure-path test where the install-local data path is blocked by a file; verify initialization fails and legacy source data remains intact.
- [x] Run `dotnet run --project CodexLauncher.Tests/CodexLauncher.Tests.csproj -c Release` and confirm the focused tests and existing suite pass (139/139).
- [x] Commit the data-mode change (`e78c1bf`).

### Task 2: Add and compile the Inno Setup installer

**Files:**
- Create: `installer/CodexLauncher.iss`
- Create: `installer/CodexLauncher.installed`
- Create: `installer/languages/ChineseSimplified.isl` from the Inno Setup official repository, retaining its translator attribution.
- Modify: `CodexLauncher.App/packages.lock.json` and `CodexLauncher.Core/packages.lock.json` only for verified win-x64 lock metadata; do not change resolved package versions.
- Modify: `.gitignore` only if a generated installer output needs an ignore rule
- Build output: `dist/CodexLauncher-v1.0.3-win-x64-setup.exe`

**Interfaces:**
- Consume the self-contained publish directory `dist/CodexLauncher-v1.0.3-win-x64-self-contained` and the installer marker constant/file `CodexLauncher.installed`.
- Produce one x64 setup executable; install into `{localappdata}\Programs\CodexLauncher` with lowest privileges.

- [x] Check the official Inno Setup site for Inno Setup 6.7.3 and its license; install its compiler because it was missing.
- [x] Write the `.iss` script with stable `AppId`, product version 1.0.3, per-user install mode, required self-contained files and notices, Start Menu shortcut, optional desktop shortcut, and installed marker.
- [x] Add uninstall-only cleanup for `{app}\portable-data`, `%LOCALAPPDATA%\CodexLauncher`, shortcut/uninstaller registrations, and only the CodexLauncher Run value; same-version install verification confirms cleanup does not run during upgrade.
- [x] Re-evaluate `win-x64` lock metadata and confirm resolved package versions remain unchanged; publish the self-contained app and compile the installer with exit code 0.
- [x] Inspect the script for accidental elevation, prerequisite download, unrelated AppData paths, and uninstall cleanup that could run during installation/upgrade.
- [x] Commit the installer source as `3464c43`; keep generated EXE as a release artifact rather than a source-controlled file.

### Task 3: Verify install, migration, upgrade, and uninstall

**Files:**
- Modify: `CodexLauncher.Tests/LauncherDataPathsTests.cs` if verification reveals a missing regression case
- Create: `installer/verify-installer.ps1` only if a reusable smoke verifier is needed

**Interfaces:**
- Consume `dist/CodexLauncher-v1.0.3-win-x64-setup.exe` and the built self-contained payload.
- Verification must run in an isolated/disposable Windows profile, or safely preserve and restore any pre-existing app-specific AppData and Run value before touching them.

- [x] Run a silent per-user install in the current account after moving the original app-specific AppData to a verified temporary backup; Setup exits 0 and creates the executable, installed marker, uninstaller registration, Start Menu shortcut, optional desktop shortcut, notices, and runtime licenses.
- [x] Launch the installed app with a prepared legacy setting; verify it imports to install `portable-data` and leaves legacy data during use. The focused migration tests verify bridge-file copying, collisions, and retry behavior.
- [x] Run an in-place same-version upgrade; Setup exits 0 and preserves app-local data and the legacy source.
- [x] Uninstall; verify exit 0, install directory and test legacy AppData removed, app-owned Run value removed, unrelated Run value preserved, and shortcuts removed.
- [x] Verify the portable resolver tests still pass and the existing portable ZIP SHA-256 remains `15EC89F0FE27ABECB2434D41CBDE774F1A7E2E86973B160666F20D8A1F11D40C`.
- [x] Record commands, exit codes, and filesystem/registry observations in the execution ledger; original AppData was restored byte-for-byte (6 files, 570345 bytes).

### Task 4: Update package documentation and checksum

**Files:**
- Modify: `README.md`
- Modify: `docs/superpowers/specs/2026-10-03-windows-installer-design.md` only for verified implementation deviations
- Build output: setup SHA-256 sidecar

**Interfaces:**
- Describe the setup installer as the normal install option and the existing ZIP as the portable option.
- State clearly that installed data is in the install directory, legacy data is imported on first launch, and uninstall permanently removes app data and supported legacy data.

- [x] Update README download/build guidance so it no longer calls the replaced duplicate ZIP the normal install option.
- [x] Preserve portable ZIP guidance and existing framework-dependent development/test instructions.
- [x] Generate the installer SHA-256 file and verify the sidecar matches a fresh `Get-FileHash` result (`097748EB05DDDA5EACCC7C41F834B5CDD79E3E83AAD2E355485FD6B9301A392E`).
- [x] Commit and push documentation after the package behavior was verified (`2afdf63`).

### Task 5: Replace the duplicate public Release asset

**Files:**
- External: GitHub Release `v1.0.3` assets at `https://github.com/jun501389541/CodexLauncher/releases/tag/v1.0.3`

**Interfaces:**
- Upload verified `CodexLauncher-v1.0.3-win-x64-setup.exe` and its SHA-256 sidecar.
- Remove only `CodexLauncher-v1.0.3-win-x64-self-contained.zip` and its sidecar; keep the portable ZIP, its sidecar, and GitHub source archives.

- [x] Update Release notes to describe the installer, data location, uninstall deletion behavior, and portable ZIP.
- [x] Upload setup EXE and checksum; verify GitHub asset metadata, published digest, and downloadable checksum; remove only the duplicate self-contained ZIP and checksum.
- [x] Verify the public release lists the setup (44,718,979 bytes; SHA-256 `097748EB05DDDA5EACCC7C41F834B5CDD79E3E83AAD2E355485FD6B9301A392E`) and portable ZIP (61,478,855 bytes); the published sidecar matches the local setup.
- [x] Capture the final release page state and report the published links and hash.
