# Slim Monitor PC v0.2.4

Native taskbar-integration update focused on eliminating flicker and making the monitor behave as part of Windows instead of as a floating overlay.

## Main change

- Slim Monitor PC is now attached as a child of the Windows `Shell_TrayWnd` taskbar.
- It is no longer a separate `TopMost` window fighting the Z-order when you change windows or press **Show desktop**.
- Because it belongs to the taskbar, it naturally follows taskbar visibility and cannot remain floating above a fullscreen game when the taskbar is behind/hidden.

## Layout

- The unified traffic + time/date block is narrower than v0.2.3.
- Extra left spacing creates a clearer gap from the battery, volume and Wi-Fi icons.
- Time/date remain visually dominant and the full `dd/MM/yyyy` date is retained.
- The far-right **Show desktop** strip remains free.

## Information menu

- The hover tooltip has been removed completely.
- Right-click now shows:
  - active Wi-Fi adapter,
  - current download speed,
  - current upload speed,
  - received data since launch,
  - sent data since launch,
  - calendar, Start with Windows, realign and exit actions.

## Preserved

- Built-in Windows-style calendar on left click.
- Start with Windows.
- Single-file, self-contained Windows x64 executable.
- Startup self-test and embedded-icon validation in CI.
