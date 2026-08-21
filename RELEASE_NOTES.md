# Slim Monitor PC v0.2.5

Windows 11 compatibility hotfix after the v0.2.4 native-child approach proved invisible on the real target system.

## Main fix

- Removes the direct `Shell_TrayWnd` child-window embedding used by v0.2.4.
- Restores a normal top-level tool window, but without the old foreground/fullscreen heuristic that caused constant hide/show flicker.
- Taskbar visibility is now determined by probing the real **Show desktop** strip with `WindowFromPoint` and confirming that the topmost window at that point belongs to `Shell_TrayWnd`.
- Normal maximized applications therefore keep Slim Monitor PC visible and stable.
- Fullscreen games/apps that actually cover the taskbar make Slim Monitor PC hide automatically.
- Auto-hidden/off-screen taskbar states also keep the monitor hidden.

## Layout and information

- Keeps the larger time and date introduced previously.
- Narrows the total block further to create a clearer gap from battery, volume and Wi-Fi icons.
- Keeps the far-right **Show desktop** strip free.
- No hover tooltip is used.
- Right-click shows active Wi-Fi adapter, current download/upload speed, session received/sent totals, calendar, Start with Windows, realign and exit actions.

## Preserved

- Built-in Windows-style calendar on left click.
- Start with Windows.
- Single-file, self-contained Windows x64 executable.
- Startup self-test and embedded-icon validation in CI.
