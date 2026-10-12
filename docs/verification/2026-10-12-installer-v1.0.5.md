# v1.0.5 installer supplement — 2026-10-12

The original v1.0.5 publication omitted the installer. The existing Inno Setup
script still hard-coded version 1.0.4 and was not invoked by that publication.

## Build and application provenance

- Extracted the already published v1.0.5 portable ZIP, first verifying its SHA-256
  `a67c46aae1368a0f3bd8049ad5585faaa56b9ade7bb1a212c1730232738b923c`.
  The application is unchanged from release commit `cd8cc59bc39725e68ca16c9aaa8f224355907421`.
- Removed only the portable-mode marker from the installation payload. The existing
  installed-mode marker is included by the installer script.
- Used official Inno Setup 6.7.3 in portable tool mode, with a verified valid
  Authenticode signature and SHA-256
  `9c73c3bae7ed48d44112a0f48e66742c00090bdb5bef71d9d3c056c66e97b732`.
- Compilation exited 0. Both the application and installer numeric file versions
  are 1.0.5.0. The build helper derives the package version from its executable,
  validates required runtime/notice files, rejects portable markers and user data,
  and generates the installer checksum.

## Installation verification

- Ran the actual installer twice into a workspace-only fixture: first installation
  and same-version in-place upgrade. Both exited 0.
- Verified installed application, SQLite native library and notices match the
  release payload hashes; the installed marker is present and portable marker absent.
- Verified synthetic settings and data files survive the in-place upgrade unchanged.
- Preserved and restored the existing uninstall registry entry and Start Menu/Desktop
  shortcuts byte-for-byte. Existing autostart remained unchanged. The user's actual
  installation and application data were not modified; no app process was launched
  or closed. The installer fixture was not uninstalled because existing uninstall
  cleanup intentionally removes legacy app data in the active Windows profile.
- Uninstall and cross-version upgrade were not rerun for this supplement. Existing
  installer behavior, stable AppId and cleanup rules are unchanged.

## Published assets and future builds

- Added `CodexLauncher-v1.0.5-win-x64-setup.exe` (44,758,913 bytes) and its `.sha256`
  file to the existing v1.0.5 Release. GitHub confirmed matching size and digest:
  `ad30ae76028e74c21b8dcbee160a5f8b8b2d7707d3f6c21357d071e9386fc899`.
- The portable assets and published application tag remain unchanged. The Release
  download instructions now describe both installation options.
- Updated the manual release workflow to build and upload both packages and their
  checksums. Removed obsolete default release identifiers; pinned and verified the
  compiler download; supplied the GitHub API User-Agent.
- The local compiler/build helper and all three workflow PowerShell blocks passed
  syntax checks. YAML structure was reviewed manually; no YAML parser was installed.
  The revised GitHub Actions workflow itself was not dispatched in this task.
