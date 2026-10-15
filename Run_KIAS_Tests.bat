@echo off
chcp 65001 >nul
cd /d "%~dp0"
set "kias_pwsh=pwsh.exe"
where pwsh.exe >nul 2>&1
if errorlevel 1 (
    set "kias_pwsh=%USERPROFILE%\.cache\codex-runtimes\codex-primary-runtime\dependencies\native\powershell\pwsh.exe"
)
"%kias_pwsh%" -NoProfile -ExecutionPolicy Bypass -File "%~dp0Tools\KiasBenchmark\launch.ps1" -WaitForStart %*
set "kias_exit=%errorlevel%"
echo.
if not defined KIAS_NO_PAUSE pause
exit /b %kias_exit%
