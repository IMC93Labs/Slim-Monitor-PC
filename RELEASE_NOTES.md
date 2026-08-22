# Slim Monitor PC v0.2.11

Emergency stability hotfix after v0.2.10 failed to show and slowed Explorer/taskbar on the real Windows 11 test system.

## Stability rollback

- Restores the proven v0.2.8 taskbar integration path.
- Removes all v0.2.10 DWM cloak/Peek/transition behavior from the active startup path.
- Removes aggressive shell coordination introduced after v0.2.8.
- Does not alter Explorer/taskbar windows beyond the already validated v0.2.8 top-level overlay behavior.

## Preserved improvements

- Keeps the stable fixed `arrow | value | unit` traffic layout so `B/s`, `KB/s`, `MB/s` and `GB/s` do not wrap or move between rows.
- Fixes the hover highlight with a lightweight 120 ms cursor-position check so the highlight cannot remain stuck after the pointer leaves the block.
- Keeps the approved block size, clock/date layout, calendar, right-click details, Start with Windows and centered executable icon.

## Validation

- Single-file, self-contained Windows x64 executable.
- Startup self-test and embedded-icon validation remain mandatory in CI.
