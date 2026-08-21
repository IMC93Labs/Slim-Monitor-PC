# Slim Monitor PC v0.2.1

Hotfix release for the v0.2 taskbar redesign.

## Fixed

- Fixed the startup crash that caused v0.2.0 to close before showing anything on the taskbar.
- Startup failures are no longer silent: an error dialog is shown and a diagnostic log is saved under `%LOCALAPPDATA%\IMC93Labs\SlimMonitorPC\startup-error.log`.
- The final EXE now uses the intended simple upload/download arrows icon (`⇅` style).

## Validation added

- The published EXE must pass a real `--self-test` startup check on the Windows GitHub Actions runner.
- The build also verifies that Windows can extract an embedded icon from the final `SlimMonitorPC.exe`.
- A release is not published if either validation fails.

## Included from v0.2.0

- Unified taskbar block with live Wi-Fi download/upload speed, current time and date.
- Built-in Windows 11-style calendar opened with left click.
- Right-click menu with **Start with Windows**, calendar, realignment and exit.
- Self-contained Windows x64 single-file executable; no separate .NET installation is required.
