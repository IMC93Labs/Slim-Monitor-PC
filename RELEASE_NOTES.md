# Slim Monitor PC v0.2.15

Focused Windows 11 **Show desktop** flicker correction. No visual, layout, colour, network, calendar or Explorer-integration changes are included in this release.

## Flicker-only change

- Keeps the stable v0.2.14 runtime path unchanged.
- Applies documented DWM attributes only to Slim Monitor PC's own HWND:
  - `DWMWA_TRANSITIONS_FORCEDISABLED`
  - `DWMWA_DISALLOW_PEEK`
  - `DWMWA_EXCLUDED_FROM_PEEK`
- The attributes are applied once when the Slim Monitor PC window handle is created and reapplied only if that handle is recreated.
- No global mouse/keyboard hook.
- No Explorer/taskbar subclassing or injection.
- No DWM cloak manipulation.
- No new polling/guard timer.
- No custom Show desktop emulation.

The purpose is to keep the overlay out of Windows Peek/Show-desktop visual transitions instead of trying to recover it after the shell has already removed it for a few frames.

## Unchanged from v0.2.14

- Size, position and taskbar colour.
- Stable fixed `arrow | value | unit` traffic rendering.
- Complete `B/s`, `KB/s`, `MB/s` and `GB/s` suffixes.
- Full `dd/MM/yyyy` date fit.
- Hover behavior.
- Built-in calendar.
- Right-click network/session details.
- Start with Windows.
- Centered executable icon.
- Single-file, self-contained Windows x64 executable.
- Startup self-test and embedded-icon validation in CI.
