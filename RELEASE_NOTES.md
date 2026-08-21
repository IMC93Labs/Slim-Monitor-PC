# Slim Monitor PC v0.2.2

Visual fit update for the unified Windows taskbar block.

## Fixed

- Corrected excessive width on systems using Windows display scaling. Taskbar coordinates are now treated as the physical pixels Windows already reports, avoiding double DPI scaling.
- The Slim Monitor PC block is now constrained to the clock/date zone and no longer reaches left far enough to cover Wi-Fi, Bluetooth or other tray icons.
- The far-right **Show desktop** strip remains free.
- The block is inset vertically so it stays fully inside the taskbar instead of appearing to protrude above it.

## Visual changes

- Time is larger and easier to read.
- Date is also larger and uses a fixed `dd/MM/yyyy` layout.
- Network speed remains on the left in a slightly smaller column so the clock/date remain visually dominant.

## Validation

The v0.2.1 release safeguards remain enabled: the Windows CI build must pass the executable startup self-test and confirm that the final single-file EXE exposes its embedded application icon before release.
