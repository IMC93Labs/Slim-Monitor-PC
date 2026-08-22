# Slim Monitor PC v0.2.10

Windows 11 shell-stability update based on frame-by-frame review of the latest real-system recording.

## Show desktop / flicker

- Removes the stacked v0.2.8 + v0.2.9 shell guards. Two independent fast timers were both trying to repair Z-order/visibility and could fight Explorer, which explains why v0.2.9 could look worse despite using a faster 12 ms guard.
- Uses one 35 ms coordinator instead of competing 35 ms + 12 ms guards.
- Adds DWM protections for forced transitions, Peek and Peek exclusion.
- Registers for Windows 11 cloak-state notifications and immediately clears shell cloaking when the taskbar should still be visible.
- Keeps the existing pre-hide `WM_WINDOWPOSCHANGING` protection and minimize rejection in the same single integration layer.
- Genuine fullscreen games/apps still hide the monitor normally.
- Writes recovery-only diagnostics to `%LOCALAPPDATA%\IMC93Labs\SlimMonitorPC\shell-state.log` if Windows still tries to hide/cloak/minimize the block, so any remaining shell-specific case can be identified instead of guessed.

## Hover

- Replaces MouseEnter/MouseLeave state tracking with direct cursor-position polling.
- The Windows 11-style highlight is now active only while the pointer is physically inside the monitor bounds, so it cannot remain stuck after the pointer leaves.

## Traffic text

- Keeps the fixed arrow/value/unit cells introduced in v0.2.9.
- `B/s`, `KB/s`, `MB/s` and `GB/s` remain on one line and the numeric values stay anchored without movement.

## Taskbar colour

- Keeps real taskbar colour sampling, but now samples robust background bands across the taskbar instead of depending on the far-right Show desktop strip, which can itself be highlighted during interaction.

## Preserved

- Approved block width and vertical fit.
- Larger clock/date.
- Built-in calendar.
- Right-click network/session details.
- Start with Windows.
- Centered executable icon.
- Single-file, self-contained Windows x64 executable.
- Startup self-test and embedded-icon validation in CI.
