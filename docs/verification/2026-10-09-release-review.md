# Desktop review and release preparation — 2026-10-09

## Scope

Review covers the desktop Core, Bridge service/store, WinForms layout and lifecycle,
and the Android wire contract. Only CodexLauncher changes are committed. Android
workspace changes remain local; no tag or GitHub Release is created.

## Review fixes

- At the twenty-device limit, invitations and pending requests remain available
  for explicit replacement. Ordinary approval still refuses a twenty-first device.
  The existing atomic replacement and rollback behavior remains unchanged.
- Reset cards are shared independently of quota-window presence. Monitoring must
  remain enabled and the snapshot available; disabled monitoring shares no cards.
- Refreshing either device list preserves checked IDs, selected IDs, keyboard focus
  and the surviving top row. Checkbox state is explicitly painted with text inset.
- Background startup restores saved connection parameters before starting the
  shared runtime monitor and timers. It stays hidden and does not overwrite saved
  settings during initialization. Cancellation is checked after installation
  discovery and before starting monitoring. Periodic diagnostics retain their
  existing connection-refresh behavior.
- Generated GraphFlow metadata is ignored. Runtime lockfile sections were restored
  after ordinary restore drift; dependency versions and release version are unchanged.

Capacity, list position, reset-card-only data and checkbox rendering regressions
were observed failing before their corresponding fixes. The asynchronous core
retry test now pumps the UI message loop with a deadline rather than synchronously
blocking a WinForms continuation.

Independent reviewers rechecked the Bridge and UI fixes without additional
blocking findings. This is an evidence-based correctness/performance review,
not a proof that every possible defect has been eliminated.

## Final verification

- `dotnet build CodexLauncher.slnx -c Release --no-restore -m:1 -v:quiet`:
  zero warnings, zero errors.
- Release test executable: **173/173 passed**.
- Release test executable `--window-resize`: **1/1 passed**, covering collapsed
  and expanded Bridge at simulated scales 1, 1.5 and 2. Each eighty-transition
  sequence applies eighty width changes; a native resize burst is coalesced and
  the final width is flushed. Scroll position, date bounds and collapse height
  remain valid. Timings vary with load; see the dedicated resize report for the
  controlled before/after comparison, approximately 35–37% lower total time.
- `dotnet restore CodexLauncher.App/CodexLauncher.App.csproj --runtime win-x64 --locked-mode`:
  passed without dependency lock changes.
- `dotnet publish CodexLauncher.App/CodexLauncher.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o artifacts/release-review/win-x64-portable --no-restore`:
  passed. Output contains the executable, SQLite native library, third-party
  notices and licenses. Build artifacts are ignored and are not committed.
- `git diff --check`: passed.

An isolated packaged startup smoke was guarded against an existing launcher
instance and therefore **was not started**. Sending a background activation to
the user's running instance would disturb its sharing state. Package construction
is verified; packaged startup and shutdown require a later manual acceptance run.

## Release boundaries

- The earlier live interop result (541 Android tests and emulator QR pairing) uses
  both current workspaces. It does not establish compatibility of every older APK.
- Android's existing Launcher parser rejects raw `usedPercent` outside 0–100,
  although the desktop contract intentionally retains raw values and clamps only
  the remaining percentage. Fix and verify the Android parser before coordinated
  release; do not silently change the desktop contract.
- Correct stale-cache labeling and preservation of the data timestamp rely on
  the local Android fixes described in the interop report. Old APKs can still label
  a cached upstream failure as fresh. Those Android fixes are outside this push.
- Real-phone LAN/firewall reachability and physical dragging across monitors at
  real 150%/200% DPI are still manual acceptance items. Simulated scaling is not
  a frame-rate or actual monitor-DPI measurement.
