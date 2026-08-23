# Slim Monitor PC v0.2.18

Focused only on eliminating the remaining Windows 11 **Show desktop** flash.

## Why the previous fixes could not remove it

Internet research confirmed that Windows **Show desktop / Win+D can still minimize or visually remove ordinary top-level windows even when they are marked always-on-top**. This is also reported against Microsoft's own PowerToys Always On Top utility. Reasserting `HWND_TOPMOST`, DWM Peek flags, owner relationships and faster recovery timers can reduce the duration, but they cannot guarantee that a normal top-level overlay is present for every compositor frame.

## v0.2.18 architecture

- Keeps the existing interactive Slim Monitor PC overlay unchanged.
- Adds a passive **visual mirror as a real `WS_CHILD` window of `Shell_TrayWnd`**.
- The mirror occupies exactly the same taskbar pixels and keeps the last valid rendered frame of Slim Monitor PC underneath the normal overlay.
- If Show desktop temporarily removes the normal top-level overlay, the taskbar child is already present and the native Windows clock/date cannot flash through.
- The mirror is input-transparent (`HTTRANSPARENT`) and never handles user interaction.
- Uses `SetParent`, `WS_CHILD`, layered/composited child-window styles and taskbar-relative coordinates. The pattern is based on the documented Win32 child-window model and the established approach used by Windows taskbar monitor projects.
- The mirror follows Explorer restarts and normal overlay geometry changes, but does not fight Z-order or call hide/show on the real overlay.

## Stability

- Removes the ineffective v0.2.16/v0.2.17 runtime guards from the active path.
- No global mouse/keyboard hooks.
- No Explorer injection or subclassing.
- No DWM cloak/Peek manipulation.
- No custom Show desktop implementation.
- No changes to size, colour, layout, hover, traffic formatting, network measurement or calendar behavior.

Single-file, self-contained Windows x64 build with startup self-test and embedded-icon validation remains unchanged.
