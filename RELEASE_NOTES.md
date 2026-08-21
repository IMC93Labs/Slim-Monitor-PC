# Slim Monitor PC v0.2.3

Taskbar-visibility and fullscreen-behavior update.

## Fixed

- Slim Monitor PC now disappears automatically when the Windows taskbar is hidden.
- Fullscreen games and applications are detected, so the taskbar overlay no longer stays on top of gameplay.
- The block comes back automatically when the taskbar/desktop becomes available again.
- Removed the repeated `SWP_SHOWWINDOW` forcing that caused a visible blink when using Windows **Show desktop**.
- Z-order is now refreshed without forcing the window visible, and only while the taskbar should be shown.
- The built-in calendar closes automatically when a fullscreen application takes over or the taskbar hides.

## Preserved from v0.2.2

- Larger time and date.
- Correct physical-pixel/DPI positioning.
- The block stays clear of Wi-Fi, volume, battery and other tray icons.
- The far-right **Show desktop** strip remains free.
- **Start with Windows**, Wi-Fi traffic measurement and the built-in calendar remain available.

## Validation

The Windows CI pipeline still requires the single-file executable to pass its startup self-test and embedded-icon verification before publication.
