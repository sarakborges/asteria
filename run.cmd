@echo off
setlocal
cd /d "%~dp0"

if not exist "logs" mkdir "logs"

for /f %%I in ('powershell -NoProfile -Command "Get-Date -Format yyyy-MM-dd_HH-mm-ss-fff"') do set "ASTERIA_RUN_STAMP=%%I"
set "ASTERIA_LOG=logs\godot-run-%ASTERIA_RUN_STAMP%.log"

set "GODOT_CMD="

if defined GODOT_EXE (
    set "GODOT_CMD=%GODOT_EXE%"
) else (
    for /f "delims=" %%G in ('where godot 2^>nul') do if not defined GODOT_CMD set "GODOT_CMD=%%G"
    if not defined GODOT_CMD (
        for /f "delims=" %%G in ('where godot4 2^>nul') do if not defined GODOT_CMD set "GODOT_CMD=%%G"
    )
)

if not defined GODOT_CMD (
    echo [Asteria] Godot executable not found.
    echo.
    echo Either add Godot to PATH or set GODOT_EXE before running:
    echo   set GODOT_EXE=C:\path\to\Godot_v4.7.2-stable_mono_win64.exe
    echo   run.cmd
    exit /b 1
)

echo [Asteria] Godot: %GODOT_CMD%
echo [Asteria] Log:   %ASTERIA_LOG%
echo.

"%GODOT_CMD%" --path "%CD%" --log-file "%ASTERIA_LOG%"
set "ASTERIA_EXIT=%ERRORLEVEL%"

echo.
echo [Asteria] Exit code: %ASTERIA_EXIT%
echo [Asteria] Log saved to: %ASTERIA_LOG%

exit /b %ASTERIA_EXIT%
