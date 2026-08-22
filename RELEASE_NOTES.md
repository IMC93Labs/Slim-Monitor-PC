# Slim Monitor PC v0.2.12

Windows 11 integration experiment based on the latest real-system recording.

## Show desktop

- Keeps the stable v0.2.8 overlay and v0.2.11 lightweight UI path.
- Registers the overlay with the Windows shell through the documented `SHAppBarMessage(ABM_NEW)` appbar API.
- Does **not** reserve desktop space (`ABM_SETPOS` is never called) and does not modify/subclass Explorer or taskbar windows.
- The purpose is to let Windows classify Slim Monitor PC as taskbar-style infrastructure instead of a normal app window during **Show desktop** transitions.
- Existing fullscreen-game hiding remains active.

## Text fit

- Keeps fixed `arrow | value | unit` rate cells.
- Slightly reduces the rate font and gives the unit column a guaranteed wider area so `B/s`, `KB/s`, `MB/s` and `GB/s` remain complete.
- Gives the clock/date column a little more width and slightly reduces only the date font to prevent the final date digit from being clipped.
- Overall block width is unchanged.

## Stability

- No DWM cloak/Peek hooks.
- No Explorer injection.
- No additional high-frequency visibility timer.
- Appbar registration is removed cleanly when the process exits or its HWND is recreated.

## Preserved

- Built-in calendar.
- Right-click network/session details.
- Start with Windows.
- Hover correction based on cursor position.
- Centered executable icon.
- Single-file, self-contained Windows x64 executable.
- Startup self-test and embedded-icon validation in CI.
