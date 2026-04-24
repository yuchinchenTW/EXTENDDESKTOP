@echo off
setlocal
cd /d "%~dp0"

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build.ps1"
set "RC=%ERRORLEVEL%"

echo.
if "%RC%"=="0" (
    echo [OK] Build succeeded. Output: "%~dp0dist"
) else (
    echo [FAIL] Build failed with exit code %RC%.
)

echo.
pause
exit /b %RC%
