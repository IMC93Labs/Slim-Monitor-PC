# Slim Monitor PC v0.2.16

Focused only on the remaining Windows 11 **Show desktop** flash seen in the latest real-machine recording.

## Root-cause change

- Removes the v0.2.15 DWM Peek/transition experiment from the runtime path.
- Keeps Slim Monitor PC as an independent top-level window instead of assigning `Shell_TrayWnd` as its owner.
- Explicitly establishes the overlay as `HWND_TOPMOST` on handle creation without activating it.
- The legacy taskbar state code is prevented from reassigning Explorer/taskbar ownership.

Microsoft documents that Show desktop raises the desktop in the Z-order while topmost windows continue to cover it. Microsoft also documents that owned windows are coupled to their owner's visibility/minimize state. This release removes that unnecessary ownership relationship rather than trying to recover the overlay after the flash has already occurred.

## Explicitly unchanged

- No global mouse/keyboard hooks.
- No Explorer/taskbar subclassing or injection.
- No DWM cloak manipulation.
- No new timers or faster polling.
- No custom Show desktop implementation.
- No changes to size, colour, layout, hover, text, network measurement or calendar.

Single-file, self-contained Windows x64 build with startup self-test and embedded-icon validation remains unchanged.
