# Changelog

## 0.2.12 — 2026-08-22

- Register the existing overlay with the documented Windows appbar service using `SHAppBarMessage(ABM_NEW)` so **Show desktop** can classify it as taskbar-style infrastructure rather than a normal application window.
- Do not call `ABM_SETPOS`, reserve desktop work area, inject into Explorer, subclass the taskbar, or add DWM cloak/Peek hooks.
- Preserve the v0.2.8 visibility/fullscreen path and the lightweight v0.2.11 hover correction.
- Keep fixed arrow/value/unit rate cells, widen the unit segment and slightly reduce the rate font so `B/s`, `KB/s`, `MB/s` and `GB/s` are not clipped.
- Give the time/date side a little more internal width and slightly reduce only the date font so the final date digit remains visible without changing the approved overall block width.
- Unregister the appbar cleanly when the HWND is destroyed or the application exits.

## 0.2.11 — 2026-08-22

- Emergency rollback after v0.2.10 failed to display and slowed the Windows taskbar on the real test machine.
- Restore the proven v0.2.8 shell integration path and remove v0.2.10 DWM cloak/Peek/transition behavior from the active runtime.
- Keep the fixed arrow/value/unit traffic renderer from later versions.
- Correct sticky hover with a lightweight 120 ms cursor-position check instead of additional shell/DWM hooks.

## 0.2.10 — 2026-08-22

- Remove the stacked v0.2.8 + v0.2.9 shell integrations so only one visibility/Z-order coordinator can interact with Explorer.
- Replace the aggressive 12 ms guard with a single 35 ms guard plus native pre-hide handling.
- Add DWM transition, Peek and Peek-exclusion protection to the taskbar overlay.
- Register for Windows 11 cloak-state notifications and immediately clear shell cloaking while the taskbar should remain visible.
- Replace event-based hover tracking with direct cursor-position polling so the Windows 11-style highlight cannot remain stuck after the pointer leaves.
- Keep the fixed arrow/value/unit traffic renderers from v0.2.9 so rate changes never wrap or move the numeric value.
- Improve taskbar colour sampling by taking a median across background bands instead of sampling only the Show desktop strip, which can itself be highlighted.
- Add recovery-only shell diagnostics at `%LOCALAPPDATA%\IMC93Labs\SlimMonitorPC\shell-state.log` for any remaining Windows-specific hide/cloak transition.

## 0.2.9 — 2026-08-22

- Sample the actual composited Windows taskbar colour from the uncovered Show desktop strip and apply that RGB value to the overlay instead of relying only on a fixed grey.
- Replace the two rate labels with fixed arrow/value/unit renderers so `B/s`, `KB/s`, `MB/s` and `GB/s` can never wrap onto another line or shift the numeric position when the scale changes.
- Give the clock/date column the remaining width while keeping the approved overall block width unchanged.
- Add an immediate visibility/minimize recovery path plus a 12 ms shell-transition guard to reduce the remaining single-frame flash when **Show desktop** is used.
- Clip only two additional pixels from the top visual region so the overlay sits below the Windows 11 shadow while continuing to cover the native clock/date underneath.
- Preserve the native-style hover highlight, fullscreen-game suppression, calendar, right-click details, Start with Windows and centered executable icon.

## 0.2.8 — 2026-08-22

- Keep the exact total width from v0.2.7 while trimming a couple of extra pixels from the top edge so the block sits fully below the Windows 11 taskbar shadow.
- Preserve bottom-edge coverage so the native Windows clock/date remains completely hidden underneath.
- Rebalance the internal columns and rate font so the complete `B/s`, `KB/s`, `MB/s` and `GB/s` suffix stays visible without widening the block.
- Add a rounded Windows 11-style hover highlight matching the nearby tray controls.
- Add a native `WM_WINDOWPOSCHANGING` guard that rejects transient hide/reorder requests caused by **Show desktop** while the taskbar remains visible.
- Add a fast 35 ms visibility/Z-order guard to recover unexpected shell transitions before they become visibly noticeable.
- Tighten fullscreen detection and explicitly ignore desktop/Explorer shell windows so real fullscreen games can still hide the monitor without treating **Show desktop** as fullscreen.

## 0.2.7 — 2026-08-22

- Keep the exact total width from v0.2.6 while lowering the block a few pixels so it stays inside the visible taskbar.
- Trim only the top edge and keep the bottom covered so the native Windows date remains hidden underneath.
- Rebalance the same width and slightly reduce the rate font so `B/s`, `KB/s`, `MB/s` and `GB/s` remain visible.
- Make `Shell_TrayWnd` the native owner of the top-level overlay without using child-window embedding.
- Ignore minimize system commands sent during **Show desktop** and recover immediately if Windows nevertheless reports a minimized state.
- Replace the taskbar probe heuristic with direct fullscreen coverage detection, avoiding hide/show transitions for normal maximized windows while still hiding over real fullscreen games.

## 0.2.6 — 2026-08-21

- Increase the unified block slightly so it fully covers the native Windows clock/date underneath instead of leaving part of the original date visible.
- Use the full taskbar height while preserving the far-right **Show desktop** strip.
- Recover automatically if Windows **Show desktop** hides or moves the overlay behind the desktop.
- Repair Z-order only when the overlay is actually no longer frontmost, avoiding continuous Z-order fighting during normal window changes.
- Compare the Explorer process ID at the taskbar probe so Windows 11 XAML taskbar surfaces are treated correctly even when their root window is not `Shell_TrayWnd`.
- Keep fullscreen coverage detection so the overlay can still hide when another process really covers the taskbar.
- Recenter the two arrows in the generated application icon.

## 0.2.5 — 2026-08-21

- Revert the v0.2.4 direct `Shell_TrayWnd` child embedding because it can be hidden by the Windows 11 XAML/composition taskbar.
- Introduce a stable top-level tool window that no longer repositions or changes Z-order on every foreground-window change.
- Replace the previous fullscreen heuristic with a real taskbar visibility probe using the free **Show desktop** strip and `WindowFromPoint`.
- Keep the monitor visible for normal maximized windows, avoiding the hide/show flicker seen in v0.2.3.
- Hide automatically when a fullscreen game/app actually covers the taskbar or when the taskbar is auto-hidden/off-screen.
- Narrow the block further to increase spacing from battery, volume and Wi-Fi icons.
- Keep hover information disabled; detailed network/session data remains in the right-click menu.

## 0.2.4 — 2026-08-21

- Rework taskbar integration so the monitor is a native child of `Shell_TrayWnd` instead of a separate `TopMost` window.
- Eliminate the window-switch / **Show desktop** flicker caused by repeatedly fighting Windows Z-order.
- Make the monitor follow the taskbar naturally: if the taskbar hides or a fullscreen game covers it, Slim Monitor PC no longer remains above the game.
- Remove the hover tooltip completely.
- Move Wi-Fi adapter, current download/upload rate and session received/sent totals into the right-click menu.
- Narrow the unified block and add extra left spacing so it sits farther away from the battery/volume/Wi-Fi icons.
- Keep the larger time/date, full `dd/MM/yyyy` date, custom calendar and Start with Windows behavior.

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
