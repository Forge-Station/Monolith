@echo off
chcp 65001 >nul

echo Запуск конвертера...
echo.

powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0convert_ogg_to_mono.ps1"

echo.
echo ==========================================
echo Работа завершена.
echo ==========================================
pause
