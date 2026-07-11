@echo off
setlocal
cd /d "%~dp0"

if not exist "logs" mkdir "logs"
set "LOG=%~dp0logs\launcher.log"

echo [%date% %time%] START_TOOL.cmd >> "%LOG%"

if not exist "TrendGovernor.Wpf.exe" (
  echo [%date% %time%] ERROR: TrendGovernor.Wpf.exe not found >> "%LOG%"
  echo Khong tim thay TrendGovernor.Wpf.exe
  pause
  exit /b 1
)

echo [%date% %time%] Launching TrendGovernor.Wpf.exe >> "%LOG%"
"%~dp0TrendGovernor.Wpf.exe"
set "EXITCODE=%ERRORLEVEL%"
echo [%date% %time%] Process exited with code %EXITCODE% >> "%LOG%"

if not "%EXITCODE%"=="0" (
  echo.
  echo TOOL KHONG KHOI DONG DUOC. MA LOI: %EXITCODE%
  echo Gui 2 file sau:
  echo   logs\launcher.log
  echo   logs\startup-crash.log
  pause
)

exit /b %EXITCODE%
