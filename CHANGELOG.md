# Changelog

## 0.2.0 — 2026-08-21

- Redesign the taskbar UI as one unified clock/date/network block.
- Overlay the native clock/date/notification zone while keeping the Show desktop strip free.
- Add current time and date beside live Wi-Fi download/upload rates.
- Add adaptive text sizing so network values are not clipped.
- Add a Windows 11-style calendar popup with month navigation and Today action.
- Left click now toggles the calendar; right click keeps the application menu.
- Remove free dragging because the block is now intentionally anchored to the clock area.
- Keep Start with Windows, Wi-Fi detection, session totals and top-most stability.
- Keep the original simple upload/download arrows icon as the EXE icon.

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
