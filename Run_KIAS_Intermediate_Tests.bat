@echo off
call "%~dp0Run_KIAS_Tests.bat" -EssentialPreflight %*
exit /b %errorlevel%
