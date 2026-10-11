@echo off
rem ============================================================
rem  HashCheck - one-click build (requires .NET 10 SDK)
rem  Output: dist\HashCheck.exe
rem  NOTE: keep this file ASCII-only; cmd.exe parses .cmd using
rem        the OEM code page and would mangle non-ASCII text.
rem ============================================================
setlocal
pushd "%~dp0"

set "DOTNET=%LOCALAPPDATA%\Microsoft\dotnet\dotnet.exe"
if not exist "%DOTNET%" set "DOTNET=dotnet"

rem Keep package cache and CLI state inside the project (offline rebuilds)
set "NUGET_PACKAGES=%~dp0.nuget\packages"
set "DOTNET_CLI_HOME=%~dp0.nuget\cli-home"
set "DOTNET_NOLOGO=1"

echo [1/3] Build (Release) ...
"%DOTNET%" build "src\HashCheck\HashCheck.csproj" -c Release
if errorlevel 1 goto fail

echo [2/3] Sync to dist ...
robocopy "src\HashCheck\bin\Release\net10.0-windows" "dist" /MIR /NJH /NJS /NDL /NP >nul
if errorlevel 8 goto fail

echo [3/3] Self test ...
"dist\HashCheck.exe" --selftest "artifacts\selftest.txt"
if errorlevel 1 goto selftestfail

echo.
echo Build OK -^> dist\HashCheck.exe
popd
exit /b 0

:selftestfail
echo.
echo WARNING: self test did not fully pass -^> artifacts\selftest.txt
popd
exit /b 1

:fail
echo.
echo Build FAILED.
popd
exit /b 1
