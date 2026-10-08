# Window resize optimization — 2026-10-09

## Changes

- Main page width has one owner; remove right anchoring and nested page AutoSize.
- `PageContentLayout` measures preferred height during its own layout, preventing the host from repeatedly measuring the entire page. Preferred height allows shrinking after collapse; allocated row heights would retain spare space.
- Suspend both host and content while changing width, then perform one explicit content layout.
- During native interactive sizing, coalesce pending widths with a 16 ms WinForms timer. ResizeEnd flushes the final width immediately. Programmatic resize remains synchronous.
- Constrain reset-card text before its new bounds are laid out; skip unchanged constraints.
- Batch device-list column updates and skip unchanged widths. The page container uses double buffering.

## Reproduction and measurement

`--window-resize` transfers the complete built UI to a normal offscreen test window with real handles. MainForm.Load and real account/network services do not run. Synthetic quota and reset cards are used.

The benchmark performs 80 alternating gradual width transitions, pumps the message loop, and records elapsed time and Layout events. It covers collapsed/expanded Bridge and geometric scales 1, 1.5, 2. These are simulated control scaling tests, not actual monitor DPI transitions or measured screen frame rates.

For the fair comparison below, temporarily restore the original root TableLayoutPanel/AutoSize/right anchor/ResizeRoot implementation, retaining the same child controls and updated test harness. Restore the optimized source in a finally block after measuring. This isolates the page-layout improvement; it is not a comparison against Git HEAD's older product features.

| Geometric scale 1; 80 transitions | Original page layout | Optimized page layout |
| --- | ---: | ---: |
| Bridge collapsed total | 3647 ms | 2374 ms |
| Bridge expanded total | 8498 ms | 5367 ms |
| Collapsed median per transition | 44.0 ms | 29.4 ms |
| Expanded median per transition | 104.8 ms | 65.6 ms |
| Collapsed Layout events | 4040 | 3600 |
| Expanded Layout events | 5160 | 4720 |

Total elapsed time falls about 35% collapsed and 37% expanded in this run. Timing varies with machine load. These synchronous transition measurements exclude any claim of smooth 60 FPS. Remaining nested child layouts still have measurable cost.

## Validation

- Build: `dotnet build CodexLauncher.Tests/CodexLauncher.Tests.csproj --no-restore -m:1 -v:minimal` — zero warnings/errors.
- `dotnet CodexLauncher.Tests/bin/Debug/net10.0-windows/CodexLauncher.Tests.dll --window-resize` — 1/1.
- Same command with `--reset-cards` — 6/6.
- Same command with `--bridge-stage5` — 23/23.
- `git diff --check` — passed.

Resize test checks scroll offset retention, viewport width, visible-card height bounds, reset-date bounds, collapse shrinkage, timer application while dragging, and immediate final-width flush. A burst of 20 size changes must produce one content-width update on release. No timing threshold is used as a correctness assertion.

Limit: physical dragging and switching monitors at real 150%/200% DPI have not been manually verified. Close any older launcher and start `CodexLauncher.App/bin/Debug/net10.0-windows/CodexLauncher.exe` to evaluate the current build. No commit/push performed in this task.
