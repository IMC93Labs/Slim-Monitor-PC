# Slim Monitor PC v0.2.13

Windows 11 integration update based on the second real-system recording.

## Show desktop

- Removes the ineffective v0.2.12 appbar registration from the active runtime path.
- Intercepts only left-clicks on the narrow Windows **Show desktop** strip at the far-right edge of the primary taskbar.
- Reproduces the show/restore desktop action by minimizing/restoring normal top-level application windows while leaving Slim Monitor PC untouched.
- This avoids the shell transition that was physically removing the overlay for several frames and exposing the native clock/date underneath.
- No Explorer injection, no taskbar subclassing, no DWM cloak/Peek logic and no high-frequency shell timer are introduced.

## Exact taskbar colour

- Samples the real composited taskbar pixels directly from the screen while excluding Slim Monitor PC itself.
- Filters out coloured icons/text and chooses the dominant neutral background colour.
- The latest recording measured the native taskbar at RGB 27,27,27 (#1B1B1B) while the previous overlay was around RGB 32,32,32; this version corrects that mismatch dynamically rather than hard-coding one grey.

## Preserved

- Stable fixed `arrow | value | unit` traffic rendering.
- Complete B/s, KB/s, MB/s and GB/s suffixes.
- Full dd/MM/yyyy date fit.
- Lightweight hover correction.
- Built-in calendar.
- Right-click network/session details.
- Start with Windows.
- Centered executable icon.
- Single-file, self-contained Windows x64 executable.
- Startup self-test and embedded-icon validation in CI.
