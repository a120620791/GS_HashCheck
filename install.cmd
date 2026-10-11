@echo off
rem ============================================================
rem  HashCheck - install to the per-user folder and register the
rem  Explorer context menu.
rem
rem  Usage:  install.cmd [target-folder]
rem          default target: %LOCALAPPDATA%\HashCheck
rem
rem  Run this by double-clicking in Explorer, NOT from inside a
rem  restricted/sandboxed session (such a session can neither write
rem  to %LOCALAPPDATA% nor write the registry).
rem
rem  NOTE: keep this file ASCII-only; cmd.exe parses .cmd using the
rem        OEM code page and would mangle non-ASCII text.
rem ============================================================
setlocal
pushd "%~dp0"

set "TARGET=%~1"
if "%TARGET%"=="" set "TARGET=%LOCALAPPDATA%\HashCheck"

echo [1/2] Copy dist -^> "%TARGET%"
if not exist "%TARGET%" mkdir "%TARGET%"
if errorlevel 1 goto fail
copy /y "dist\*.*" "%TARGET%" >nul
if errorlevel 1 goto fail

echo [2/2] Register Explorer context menu
"%TARGET%\HashCheck.exe" --register
if errorlevel 1 goto fail

echo.
echo Installed : "%TARGET%\HashCheck.exe"
echo Menu      : right-click a file, then "Show more options" on Windows 11
echo To remove : "%TARGET%\HashCheck.exe" --unregister
popd
exit /b 0

:fail
echo.
echo Install FAILED.
popd
exit /b 1
