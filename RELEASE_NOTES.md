# Slim Monitor PC v0.2.6

Taskbar layout and **Show desktop** recovery update based on real Windows 11 testing.

## Fixed

- The unified traffic/time/date block is slightly larger again so it fully covers the native Windows clock/date underneath instead of leaving part of the original date visible.
- The block now uses the full taskbar height while still keeping the far-right **Show desktop** strip free.
- Windows **Show desktop** can no longer leave Slim Monitor PC permanently hidden: the app now checks its real native visibility and repairs its Z-order only when Windows has actually moved it behind the desktop.
- The taskbar-front detection now compares the owning Explorer process rather than requiring every Windows 11 XAML taskbar surface to have `Shell_TrayWnd` as its root window.
- Normal window switching therefore does not trigger constant hide/show cycles, while a fullscreen game that really covers the taskbar can still hide the monitor.

## Icon

- Recentered the two upload/download arrows inside the executable icon so the symbol is visually centered instead of shifted to the right.

## Preserved

- Larger time and date.
- No hover tooltip.
- Right-click network/session information.
- Built-in calendar on left click.
- Start with Windows.
- Single-file, self-contained Windows x64 executable.
- Startup self-test and embedded-icon validation in CI.
