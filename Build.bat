@echo off
setlocal EnableDelayedExpansion

rem ==================================================
rem File: Build.bat
rem Builds all Visual Studio projects in the current
rem directory using MSBuild and the specified
rem Visual Studio toolchain.
rem
rem Copyright (c) 2024-2026 Pavel Bashkardin
rem Licensed under the MIT License.
rem https://github.com/ng256/IniFile/blob/main/LICENSE
rem ==================================================

rem ==================================================
rem Configuration
rem ==================================================

rem Build configuration: Debug / Release
set CONFIGURATION=Debug

rem Visual Studio developer command script
set VSDEVCMD="C:\Program Files\Microsoft Visual Studio\2022\Community\Common7\Tools\VsDevCmd.bat"

rem MSBuild additional arguments
set MSBUILD_ARGS=/restore /p:Platform=x64

rem Project search pattern
set PROJECT_PATTERN=*.csproj

rem ==================================================
rem Initialize Visual Studio environment
rem ==================================================

echo Setting up Visual Studio environment...

if not exist %VSDEVCMD% (
    echo VsDevCmd.bat not found:
    echo %VSDEVCMD%
    pause
    exit /b 1
)

call %VSDEVCMD% -no_logo
if %errorlevel% neq 0 (
    echo Failed to initialize VS environment.
    pause
    exit /b %errorlevel%
)

where msbuild >nul 2>nul
if %errorlevel% neq 0 (
    echo msbuild.exe not found after initializing VS environment.
    pause
    exit /b 1
)

rem ==================================================
rem Find and build projects
rem ==================================================

set SCRIPT_DIR=%~dp0
set FOUND_PROJECTS=0
set BUILD_FAILED=0

echo.
echo Searching projects:
echo %SCRIPT_DIR%%PROJECT_PATTERN%
echo.

for %%P in ("%SCRIPT_DIR%%PROJECT_PATTERN%") do (
    set FOUND_PROJECTS=1

    echo ==============================================
    echo Building: %%~nxP
    echo Configuration: %CONFIGURATION%
    echo ==============================================

    msbuild "%%P" /nologo /p:Configuration=%CONFIGURATION% %MSBUILD_ARGS%

    if !errorlevel! neq 0 (
        echo.
        echo FAILED: %%~nxP
        set BUILD_FAILED=1
    ) else (
        echo.
        echo SUCCESS: %%~nxP
    )

    echo.
)

if %FOUND_PROJECTS% equ 0 (
    echo No projects found.
    pause
    exit /b 1
)

if %BUILD_FAILED% neq 0 (
    echo.
    echo Build failed.
    pause
    exit /b 1
)

echo.
echo All projects built successfully.
pause

endlocal
