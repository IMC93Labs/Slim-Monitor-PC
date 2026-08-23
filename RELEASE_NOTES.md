# Slim Monitor PC v0.2.19

Emergency stability rollback after the v0.2.18 taskbar-child experiment caused Explorer/taskbar hangs on the real Windows 11 test machine.

## What changed

- Completely removes the v0.2.18 passive mirror and every `SetParent`/`WS_CHILD` cross-process taskbar attachment introduced by that release.
- Restores the previously stable v0.2.17 runtime path.
- Keeps the existing visual layout, traffic formatting, hover, calendar and startup behavior unchanged.
- The remaining Show Desktop flash is intentionally left unresolved in this recovery release rather than risking Explorer stability.

## Why the v0.2.18 approach was abandoned

Microsoft documents that cross-process `SetParent` can force DPI-awareness resets and may produce unexpected behavior when the parent and child use different DPI-awareness modes. Windows 11 also no longer exposes the old user-extensible taskbar toolbar model as a supported way to host arbitrary third-party UI in the taskbar.

The project will not use cross-process taskbar parenting again for this feature.

Single-file, self-contained Windows x64 build with startup self-test and embedded-icon validation remains enabled.