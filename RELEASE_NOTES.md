# Slim Monitor PC v0.2.7

Real Windows 11 refinement for height, rate units and **Show desktop** stability.

## Fixed

- Keeps the same total width approved in v0.2.6.
- Moves the overlay a few pixels down and reduces only its top edge so it no longer protrudes above the visible taskbar.
- Keeps the bottom edge covered so the native Windows date remains hidden underneath.
- Gives the network-rate column more usable space and uses a slightly smaller rate font so `B/s`, `KB/s`, `MB/s` and `GB/s` remain visible instead of being clipped.
- Makes the taskbar the native owner of the overlay without turning the overlay into a child window. This is intended to keep Windows **Show desktop** from minimizing/hiding it during the desktop animation while avoiding the Windows 11 XAML problem seen in v0.2.4.
- Ignores explicit minimize system commands for the taskbar overlay and immediately recovers if Windows nevertheless reports a minimized state.
- Replaces the taskbar probe heuristic with direct fullscreen coverage detection: normal maximized windows no longer cause hide/show transitions, while a real fullscreen game covering the taskbar still hides the monitor.

## Preserved

- Full unified traffic + time + date block.
- No hover tooltip.
- Right-click network/session information.
- Built-in calendar on left click.
- Start with Windows.
- Centered upload/download executable icon.
- Single-file, self-contained Windows x64 executable.
- Startup self-test and embedded-icon validation in CI.
