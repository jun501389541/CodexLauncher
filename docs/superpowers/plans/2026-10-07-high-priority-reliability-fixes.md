# High Priority Reliability Fixes Implementation Plan

> **Executor:** Inline implementation in this session using the approved design and TDD.

**Goal:** Prevent transient TUN/settings I/O from destabilizing startup and report a route as confirmed only when bound to the current launcher-started Codex instance.

**Architecture:** Keep the existing Core/App split. Core exposes unknown TUN reads, structured settings load results, and a small process-bound launch-route tracker. App merges valid TUN facts, contains timer failures, retries failed core initialization, and selects active versus recommended route evidence.

**Tech Stack:** .NET 10, WinForms, existing console test harness.

**Spec:** User-approved in-chat design on 2026-10-07.

## Global Constraints

- Preserve the existing settings JSON shape and default behavior for absent or corrupt JSON.
- Do not infer TUN-off from an I/O failure.
- External Codex processes remain unconfirmed.
- Keep proxy route probing and security checks intact.
- Keep changes on `codexlauncher/fix-high-priority`.

## Review Focus

- A locked TUN file must not escape a timer callback or create a false “off” value.
- Unknown TUN state must not enable launch when a dual-TUN conflict cannot be ruled out.
- A changed recommended route must not overwrite the active instance's recorded launch route.
- A reused PID with a different process start time must not inherit route confirmation.
- A locked settings file must remain byte-for-byte unchanged and core initialization must be retryable.

---

### Task 1: TUN read failures

**Files:** `CodexLauncher.Core/TunModeDiscovery.cs`, `CodexLauncher.Core/AccessHealth.cs`, `CodexLauncher.App/MainForm.cs`, `CodexLauncher.Tests/Program.cs`.

- [x] Add a regression test that locks an existing config and asserts the corresponding TUN value is unknown.
- [x] Run the targeted test and confirm the expected failure.
- [x] Make TUN values nullable, merge unknown values with the last valid state, display unknown explicitly, and fail closed for launch while conflict status is unknown.
- [x] Add containment for unexpected timer exceptions so the next tick retries.
- [ ] Rerun the targeted tests and full test suite after the final fail-closed launch guard.

### Task 2: Settings recovery

**Files:** `CodexLauncher.Core/LauncherSettings.cs`, `CodexLauncher.App/MainForm.cs`, `CodexLauncher.Tests/Program.cs`.

- [x] Add a regression test that locks settings, checks a structured unavailable result, and verifies unchanged contents.
- [x] Run the targeted test and confirm the expected failure.
- [x] Add a structured load result that distinguishes missing, loaded, corrupt, and temporarily unavailable settings.
- [x] Clear a failed cached core-initialization task after completion, then test a failed attempt followed by a successful retry.
- [x] Run the targeted tests and full test suite.

### Task 3: Route evidence

**Files:** `CodexLauncher.Core/RuntimeMonitor.cs`, new `CodexLauncher.Core/LaunchRouteTracker.cs`, `CodexLauncher.App/MainForm.cs`, `CodexLauncher.Tests/Program.cs`.

- [x] Add tests for external/unmatched instances, a recorded instance, route recommendation changes, and PID reuse.
- [x] Run the targeted tests and confirm the expected failures.
- [x] Record a launch route only after successful launcher activation and verified discovery; bind it to PID and start time.
- [x] Keep probing the active instance route when known; otherwise probe the recommended route while leaving its confirmation false.
- [x] Run the targeted tests and full test suite.

### Task 4: Final verification

- [ ] Rebuild `CodexLauncher.slnx` in Release after the final fail-closed launch guard.
- [ ] Rerun the complete default test executable; do not run real-account/Mihomo integration tests.
- [ ] Reinspect the final diff and confirm the original `main` worktree remains unchanged.

**Verification status:** The earlier Release build and 145/145 test run passed before the final change that disables launch while the latest TUN read is unavailable. Revalidation is pending because the automatic approval service repeatedly failed to start the local test command; no workaround was used.
