# Slim Monitor PC v0.2.20

Focused only on eliminating the remaining Windows 11 **Show desktop** flash without touching Explorer.

## Root cause confirmed by research

Windows' native **Show desktop / Win+D** is allowed to visually remove ordinary top-level windows even when they are topmost. This is also reported against Microsoft's own PowerToys Always On Top utility, so repeatedly reasserting `HWND_TOPMOST`, DWM flags or faster recovery timers cannot guarantee a zero-frame result.

## v0.2.20 approach

- Slim Monitor PC now extends only a few pixels farther right to cover the native **Show desktop** strip while keeping the approved traffic/clock block at exactly the same screen coordinates and width.
- That tiny rightmost strip is rendered locally with hover and pressed feedback so the click is visibly acknowledged.
- Clicking it no longer invokes the native Windows Show desktop transition.
- Instead, Slim Monitor PC enumerates normal visible top-level application windows with the documented `EnumWindows` API and minimizes them asynchronously with `ShowWindowAsync`, excluding Slim Monitor PC itself, shell infrastructure, hidden/cloaked windows and pre-existing minimized windows.
- A second click restores only the windows that Slim Monitor PC minimized, preserving maximized state where applicable.
- `DWMWA_CLOAKED` is used only as a read-only filter so windows belonging to other virtual desktops/shell surfaces are not touched.

## Safety / stability

- No global mouse or keyboard hooks.
- No `SetParent`, `WS_CHILD` or cross-process taskbar embedding.
- No Explorer injection or subclassing.
- No DWM cloak/Peek modification.
- No custom shell process manipulation.
- No faster visibility polling.
- If the user presses **Win+D** on the keyboard, Windows still runs its native Show desktop behavior; v0.2.20 specifically replaces the mouse click on the far-right taskbar strip, which is the path used in the real-machine tests.

Single-file, self-contained Windows x64 build with startup self-test and embedded-icon validation remains enabled.
