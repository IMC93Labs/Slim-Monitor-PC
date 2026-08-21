@echo off
setlocal
cd /d "%~dp0"
where dotnet >nul 2>nul
if errorlevel 1 (
  echo.
  echo .NET 8 SDK was not found.
  echo Install the .NET 8 SDK and run this file again.
  echo.
  pause
  exit /b 1
)

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0generate-icon.ps1"
if errorlevel 1 (
  echo.
  echo ERROR: icon generation failed.
  pause
  exit /b 1
)

if exist release rmdir /s /q release

dotnet publish SlimMonitorPC.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:PublishTrimmed=false -p:EnableCompressionInSingleFile=true -o release
if errorlevel 1 (
  echo.
  echo ERROR: build failed.
  pause
  exit /b 1
)

start /wait "" "%~dp0release\SlimMonitorPC.exe" --self-test
if errorlevel 1 (
  echo.
  echo ERROR: startup self-test failed.
  pause
  exit /b 1
)

echo.
echo Build complete and startup self-test passed:
echo %~dp0release\SlimMonitorPC.exe
echo.
pause
