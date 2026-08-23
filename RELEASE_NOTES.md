# Slim Monitor PC v1.0.0-rc1

Clean rewrite focused on eliminating the Windows 11 taskbar flash instead of adding more patches to the v0.2.x overlay stack.

## Why the project was rewritten

The v0.2.x line accumulated competing mechanisms: taskbar ownership, repeated TOPMOST promotion, short guard timers, WndProc hide/minimize interception, DWM experiments, custom Show Desktop handling and a cross-process child-window experiment. Some reduced the flash, but the mechanisms could fight each other and several variants affected Explorer stability.

Research against Microsoft Win32 documentation and the mature NetSpeedTray 2.x taskbar monitor identified the architecture we had never tested in isolation: a top-level widget **owned by the taskbar**, with Z-order established once and then left alone.

## New architecture

- Only the new `src/*.cs` files are compiled. Every v0.2.x source file remains excluded from the binary.
- Slim Monitor PC is a top-level `WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE` window.
- `Shell_TrayWnd` is assigned as the widget owner through `GWLP_HWNDPARENT`; this is ownership, not cross-process parenting.
- `HWND_TOPMOST` is asserted exactly once when the ownership relationship is first established, and once again only if Explorer restarts and the taskbar HWND changes.
- Normal position updates use `SWP_NOZORDER | SWP_NOOWNERZORDER`, so steady state never fights Windows for Z-order.
- Shell state is event-driven with `SetWinEventHook` (`WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS`) and debounced before one authoritative refresh.
- A slow 1.5-second safety timer exists only as recovery; it never promotes Z-order.
- Fullscreen apps hide the widget while Explorer/desktop shell surfaces are explicitly excluded from fullscreen classification.
- The widget paints background, traffic values, time and date in one buffered surface, removing child-control repaint races.

## Explicitly removed/banned

- No `SetParent` / `WS_CHILD` attachment to Explorer.
- No global mouse or keyboard hooks.
- No custom Show Desktop implementation.
- No DWM cloak/Peek modification.
- No 12/35/200 ms Z-order polling loops.
- No repeated `HWND_TOPMOST` promotion.
- No WndProc battle against minimize/hide messages.

CI now rejects the release if these banned primitives reappear in the clean source tree or if more than one TOPMOST seating path is introduced.

## User-facing behavior retained

- Wi-Fi download and upload speed with stable B/s, KB/s, MB/s and GB/s fields.
- Large time and date in the taskbar clock area.
- Native-like hover highlight.
- Calendar on left click.
- Traffic/session information and options on right click.
- Start with Windows.
- Single-file self-contained Windows x64 executable with embedded icon.

This is an RC because the Windows runner cannot reproduce the real Windows 11 **Show desktop** compositor transition. The architecture, build, startup, icon and regression guards are validated automatically; the final flash check must be performed on the real desktop where the issue was reproduced.
