@echo off
setlocal
cd /d "%~dp0"
if exist "TrendGovernor.Wpf.exe" (
  start "" "TrendGovernor.Wpf.exe"
  exit /b 0
)
echo Khong tim thay TrendGovernor.Wpf.exe
pause
