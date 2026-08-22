# Slim Monitor PC v0.2.14

Emergency stability hotfix after v0.2.13 caused Explorer/taskbar hangs on the real Windows 11 system.

## Stability recovery

- Removes the global mouse hook used to intercept the Windows **Show desktop** strip.
- Removes the custom minimize/restore emulation introduced in v0.2.13.
- Removes screen-pixel taskbar colour sampling from the runtime path.
- Returns to the proven v0.2.8 shell behavior plus the lightweight v0.2.11 UI refinement.
- Does not inject into Explorer, subclass taskbar windows, use DWM cloak/Peek APIs, register an appbar or install any global input hook.

## Safe colour correction

- On dark taskbars, uses the measured native Windows 11 taskbar colour from the real test system: RGB 27,27,27 (`#1B1B1B`).
- No live screen sampling or shell calls are needed for the colour correction.

## Preserved

- Stable fixed `arrow | value | unit` traffic rendering.
- Complete `B/s`, `KB/s`, `MB/s` and `GB/s` suffixes.
- Full `dd/MM/yyyy` date fit.
- Lightweight hover correction.
- Built-in calendar.
- Right-click network/session details.
- Start with Windows.
- Centered executable icon.
- Single-file, self-contained Windows x64 executable.
- Startup self-test and embedded-icon validation in CI.

Known limitation: the small native Windows **Show desktop** flash can still occur. Stability takes priority in this release; no shell interception is used to hide that transition.
