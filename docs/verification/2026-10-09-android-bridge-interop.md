# Android / CodexLauncher live interoperability — 2026-10-09

## Scope

Desktop worktree: `analysis/worktrees/fix-high-priority`; Android worktree: `E:/Workspace/project/AI-Usage-Monitor/repo`. Both had existing uncommitted changes; those were retained. No commit/push.

The desktop test project runs real Kestrel HTTPS and the production pairing/device/grant services, with synthetic account data, a temporary secured identity directory and loopback port 43190. Only this test project auto-approves after three seconds. Production approval and Android pairing protocol are unchanged.

## Findings and fixes

1. Desktop can retain recently read quota after an upstream failure, returning `status: OK`, `isStale: false` and a nonempty `errorCode`. Android ignored that error and treated retained quota as a new successful reading. The live test failed with expected STALE / actual OK. Android now labels retained quota STALE when that error marker is present.
2. Android parsed the envelope's `updatedAt` rather than `dataTimestamp`, and its refresh manager then unconditionally overwrote the result time with the phone's current time. It now prefers the actual data timestamp and preserves a valid Bridge result timestamp when saving. The paired-computer `lastSeen` still reflects the current contact time.

Modified Android production paths: `BridgeUsageParser.java` and `AccountRefreshManager.java`. Added focused parser/storage regression assertions and opt-in `LiveLauncherInteropTest.java`.

## Live test coverage

The Java test calls the Android production invitation parser, certificate-pinned pairing client and quota data source against the current desktop test service. It verifies:

- An incorrect certificate pin is rejected.
- PendingApproval progresses to approval and acknowledgment.
- Authorized quota reads return 78% five-hour / 19% weekly remaining.
- Multiple cards: authoritative availableCount 3, two returned details, expiry and explicit no-expiry.
- Original reset-credit identifiers do not reach cached Android JSON.
- Cache round-trip and stale copy retain reset details.
- Actual authenticated POST refresh is accepted.
- Zero cards, count-only cards, missing data and malformed optional counts remain distinct.
- An upstream failure retains quota/cards with an honest stale state.
- Changing the desktop account rejects the previous grant; revoking the device rejects its token.

Test controls (`command.txt`, `command-result.txt`) exist only in the temporary secured fixture directory. They are not network routes. No real account credentials, invitations or tokens were printed or committed.

## Validation evidence

- Desktop build: zero warnings/errors.
- Desktop `--bridge-protocol-client`: 2/2 passed, including actual HTTPS auth/cache/refresh/account isolation and approval/replay/expiry routes.
- Android final independent build: 541 tests, zero failures/errors/skips; includes the live HTTPS test.
- Android Debug APK assembled and installed on emulator-5554 while preserving existing data.
- Both repositories: `git diff --check` passed.

An independent Gradle build output was used to avoid reports being overwritten by another Android verification run:

`E:/Workspace/project/AI-Usage-Monitor/.debug-runtime/current-launcher-interop-build/app/`

Reproduce with the trusted Gradle 9.1.0 distribution, existing dependency cache, and `current-launcher-interop.init.gradle`; set `CODEX_INTEROP_DIR` to the running test service's temporary directory. Without that environment variable, the external-service test intentionally skips.

## Emulator evidence and limits

The emulator scanned an actual PNG QR invitation, completed delayed approval, queried synthetic quota and displayed 78% / 19%, available 3 resets, two cards, dated expiry, no expiry, and the incomplete-detail notice. After an upstream failure and an app restart it retained the cards and displayed “上次读数”. The final timestamp preservation is additionally verified through parser and refresh-manager regressions.

Restarting this disposable fixture creates a new identity/certificate. An older test pairing correctly rejects the new certificate; this is not the production identity persistence path. The emulator also contains a separate 43191 test account from another verification run; it is outside this fixture's acceptance scope and was not modified by this task.

Not verified: real-phone Wi-Fi reachability/firewall, real Codex upstream credentials, and desktop GUI manual approval in this cross-project run. The Android query data source reads the desktop cache; the server's immediate-refresh POST was verified separately, so this report does not claim the phone's query button forces an upstream refresh.
