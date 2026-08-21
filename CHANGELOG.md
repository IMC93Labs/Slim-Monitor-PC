# Changelog

## 0.2.3 — 2026-08-21

- Hide Slim Monitor PC whenever the Windows taskbar is not actually visible, including auto-hide states.
- Detect fullscreen foreground applications and games and hide the overlay so it never remains above gameplay.
- Restore the overlay automatically when returning to the desktop or when the taskbar becomes visible again.
- Stop forcing `SWP_SHOWWINDOW` every 500 ms; this removes the visible flicker caused by Windows **Show desktop** transitions.
- Keep only a low-frequency Z-order refresh while the taskbar is visible.
- Close the custom calendar automatically if the taskbar becomes hidden or a fullscreen application takes over.
- Preserve the v0.2.2 layout fixes: larger time/date, corrected DPI sizing and no overlap with Wi-Fi/volume/battery tray icons.

## 0.2.2 — 2026-08-21

- Fix the unified taskbar block becoming too wide on displays using Windows DPI scaling.
- Stop applying DPI scaling twice to physical taskbar coordinates returned by Windows.
- Keep the overlay inside the clock/date area so it no longer covers Wi-Fi, Bluetooth or other tray icons.
- Keep the far-right **Show desktop** strip free.
- Increase the clock and date font sizes for better readability.
- Reduce the network-rate column slightly so traffic information stays secondary to time/date.
- Keep the v0.2.1 startup self-test and embedded-icon validation.

## 0.2.1 — 2026-08-21

- Fix the v0.2.0 startup crash that closed the app before anything appeared on the taskbar.
- Replace the broken startup path with the stable unified `TaskbarMonitorForm`.
- Add visible fatal-error reporting plus `%LOCALAPPDATA%\IMC93Labs\SlimMonitorPC\startup-error.log` so startup failures no longer happen silently.
- Add a `--self-test` startup mode and require it to pass in GitHub Actions before a release can be published.
- Generate and embed the simple upload/download arrows icon (`⇅` style) used for Slim Monitor PC.
- Add CI verification that the final published EXE exposes an embedded Windows icon.
- Keep the unified clock/date/network block, built-in calendar and Start with Windows behavior introduced in v0.2.0.

## 0.2.0 — 2026-08-21

- Redesign the taskbar UI as one unified clock/date/network block.
- Overlay the native clock/date/notification zone while keeping the Show desktop strip free.
- Add current time and date beside live Wi-Fi download/upload rates.
- Add adaptive text sizing so network values are not clipped.
- Add a Windows 11-style calendar popup with month navigation and Today action.
- Left click now toggles the calendar; right click keeps the application menu.
- Remove free dragging because the block is now intentionally anchored to the clock area.
- Keep Start with Windows, Wi-Fi detection, session totals and top-most stability.

## 0.1.1 — 2026-08-21

- Reduced the meter width from 180 px to a compact ~96 px layout.
- Download and upload are now displayed on two lines for better readability in less space.
- Added left-button dragging directly over the taskbar, including over existing taskbar icons.
- The chosen taskbar position is remembered between launches.
- Added **Reset position** to the right-click menu.
- Fixed the meter being covered by the Windows taskbar after clicking it by continuously preserving its top-most Z-order.
- A normal left click no longer performs any hide/close action; left drag only moves the meter.

## 0.1.0 — 2026-08-21

- Initial public release.
- Live Wi-Fi download and upload speed meter for the Windows taskbar.
- Automatic B/s, KB/s, MB/s and GB/s units.
- Active Wi-Fi adapter detection without packet capture or extra drivers.
- Session download/upload totals in the tooltip.
- Windows light/dark taskbar color adaptation.
- Optional **Start with Windows** setting using the current-user startup registry key.
- Single-instance protection.
- Self-contained Windows x64 single-file executable with embedded application icon.
