# Slim Monitor PC v0.2.9

Pixel-level Windows 11 integration refinement based on the latest real-system testing.

## Taskbar colour matching

- The overlay no longer relies only on a fixed dark/light grey.
- It samples the real composited taskbar colour from the uncovered **Show desktop** strip and applies that exact RGB value to the block.
- This follows Windows tint/transparency changes and makes the normal, non-hover state blend much more closely with the native taskbar.

## Stable traffic text

- Download/upload are now rendered in fixed arrow/value/unit cells instead of one wrapping label.
- `B/s`, `KB/s`, `MB/s` and `GB/s` keep a dedicated unit column, so changing scale cannot push text onto another row.
- Numeric values stay right-aligned against the same unit position, preventing the visible left/right movement seen when the scale changes.
- The clock/date column receives the remaining width so the full date remains readable without changing the approved overall block width.

## Show desktop refinement

- Adds an immediate `VisibleChanged`/minimize recovery path on top of the v0.2.8 native window-position guard.
- Runs a short 12 ms shell-transition guard while the taskbar should be visible, reducing the remaining single-frame flash when **Show desktop** is used.
- Genuine fullscreen applications still suppress the overlay instead of forcing it over a game.

## Visual fit

- Keeps the v0.2.8 width unchanged.
- Clips only two additional pixels from the top visual region so the block remains below the Windows 11 shadow while continuing to cover the native clock/date underneath.
- Preserves the Windows 11-style hover highlight, calendar, right-click details, Start with Windows, centered executable icon and single-file packaging.
