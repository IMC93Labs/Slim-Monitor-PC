# Slim Monitor PC v0.2.17

Focused only on the remaining Windows 11 **Show desktop** flash reproduced again in the latest real-machine recording.

## Root cause addressed

- The base overlay owns a 200 ms shell timer that calls `MaintainShellState()`.
- That method deliberately calls `SW_HIDE` when it believes a foreign fullscreen window covers the taskbar.
- During **Show desktop**, Windows temporarily foregrounds shell/desktop surfaces. On the real machine this can overlap the same fullscreen heuristic and make Slim Monitor PC hide itself, then reappear on a later timer tick — exactly the short native-clock flash visible in the recordings.

## v0.2.17 change

- Replaces only that legacy 200 ms shell-timer cadence; the interval is not made faster.
- While the foreground is the Windows desktop/shell (`GetShellWindow`, `Progman`, `WorkerW`, taskbar or known Windows 11 shell bridge/input-site surfaces), the already-visible overlay is left untouched.
- For normal applications and genuine fullscreen games, the exact existing `MaintainShellState()` implementation continues to run, preserving fullscreen-game hiding.
- Uses documented `GetShellWindow`, `GetClassName`, `GetAncestor` and `GetWindowThreadProcessId` APIs only.

## Explicitly unchanged

- No global mouse/keyboard hooks.
- No Explorer/taskbar injection or subclassing.
- No DWM cloak/Peek manipulation.
- No custom Show desktop implementation.
- No visual, colour, size, layout, hover, traffic text, network measurement or calendar changes.

Single-file, self-contained Windows x64 build with startup self-test and embedded-icon validation remains unchanged.
