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

if exist release rmdir /s /q release

dotnet publish SlimMonitorPC.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:PublishTrimmed=false -p:EnableCompressionInSingleFile=true -o release
if errorlevel 1 (
  echo.
  echo ERROR: build failed.
  pause
  exit /b 1
)

echo.
echo Build complete:
echo %~dp0release\SlimMonitorPC.exe
echo.
pause
