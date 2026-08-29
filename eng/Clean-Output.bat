@echo off
setlocal EnableExtensions DisableDelayedExpansion

set "SCRIPT_DIRECTORY=%~dp0"
set "DRY_RUN="
if /i "%~1"=="--dry-run" (
    set "DRY_RUN=1"
    if not "%~2"=="" goto :Usage
    goto :ArgumentsParsed
)
if not "%~1"=="" goto :Usage

:ArgumentsParsed
for %%I in ("%SCRIPT_DIRECTORY%..") do set "REPOSITORY_ROOT=%%~fI"
set "BUILD_PROPS=%REPOSITORY_ROOT%\Directory.Build.props"
set "OUTPUT_DIRECTORY=%REPOSITORY_ROOT%\output"

if not exist "%BUILD_PROPS%" (
    echo ERROR: Directory.Build.props was not found: "%BUILD_PROPS%"
    exit /b 1
)

set "APP_VERSION="
for /f "usebackq tokens=2 delims=>" %%A in (`findstr /c:"AppVersion" "%BUILD_PROPS%"`) do (
    if not defined APP_VERSION for /f "tokens=1 delims=<" %%B in ("%%A") do set "APP_VERSION=%%B"
)
if not defined APP_VERSION (
    echo ERROR: AppVersion could not be read from Directory.Build.props.
    exit /b 1
)
echo(%APP_VERSION%| findstr /r /x "[0-9][0-9]*\.[0-9][0-9]*\.[0-9][0-9]*" >nul
if errorlevel 1 (
    echo ERROR: AppVersion "%APP_VERSION%" is not a semantic version.
    exit /b 1
)

if not exist "%OUTPUT_DIRECTORY%\" (
    echo Nothing to clean: "%OUTPUT_DIRECTORY%" does not exist.
    exit /b 0
)

set "LATEST_RELEASE_ZIP="
set "LATEST_RELEASE_VERSION="
for /f "delims=" %%F in ('dir /b /a-d /o-d "%OUTPUT_DIRECTORY%\DeepSeekHarnessDesktop-*-win-x64.zip" 2^>nul') do (
    if not defined LATEST_RELEASE_ZIP call :SetLatestRelease "%%F"
)

echo Output directory: "%OUTPUT_DIRECTORY%"
echo Source version:   %APP_VERSION%
if defined LATEST_RELEASE_ZIP (
    echo Latest release:  %LATEST_RELEASE_ZIP%
) else (
    echo Latest release:  none
)
if defined DRY_RUN echo Mode:            dry run
echo.

set "CLEAN_FAILED="
for /d %%D in ("%OUTPUT_DIRECTORY%\*") do (
    if exist "%%~fD\" (
        if /i "%%~nxD"=="publish" (
            call :CleanPublishDirectory "%%~fD"
        ) else (
            call :RemoveDirectory "%%~fD"
        )
    )
)

for %%F in ("%OUTPUT_DIRECTORY%\*") do (
    if not exist "%%~fF\" (
        if /i not "%%~nxF"=="%LATEST_RELEASE_ZIP%" call :RemoveFile "%%~fF"
    )
)

if defined CLEAN_FAILED (
    echo.
    echo ERROR: One or more output items could not be removed.
    exit /b 1
)

echo.
if defined DRY_RUN (
    echo Dry run completed. Run without --dry-run to apply the cleanup.
) else (
    echo Output cleanup completed.
)
exit /b 0

:SetLatestRelease
set "LATEST_RELEASE_ZIP=%~1"
set "LATEST_RELEASE_VERSION=%LATEST_RELEASE_ZIP:~23,-12%"
exit /b 0

:CleanPublishDirectory
set "KEEP_PUBLISH_DIRECTORY="
if exist "%~1\%APP_VERSION%\" set "KEEP_PUBLISH_DIRECTORY=1"
if defined LATEST_RELEASE_VERSION if exist "%~1\%LATEST_RELEASE_VERSION%\" set "KEEP_PUBLISH_DIRECTORY=1"
if not defined KEEP_PUBLISH_DIRECTORY (
    call :RemoveDirectory "%~1"
    exit /b 0
)
for /d %%V in ("%~1\*") do (
    if exist "%%~fV\" (
        if /i not "%%~nxV"=="%APP_VERSION%" (
            if /i not "%%~nxV"=="%LATEST_RELEASE_VERSION%" call :RemoveDirectory "%%~fV"
        )
    )
)
for %%F in ("%~1\*") do (
    if not exist "%%~fF\" call :RemoveFile "%%~fF"
)
exit /b 0

:RemoveDirectory
if defined DRY_RUN (
    echo [remove directory] "%~1"
    exit /b 0
)
echo [remove directory] "%~1"
rd /s /q "\\?\%~1" 2>nul
if exist "%~1\" (
    echo ERROR: Failed to remove directory "%~1".
    set "CLEAN_FAILED=1"
)
exit /b 0

:RemoveFile
if defined DRY_RUN (
    echo [remove file]      "%~1"
    exit /b 0
)
echo [remove file]      "%~1"
del /f /q "\\?\%~1" 2>nul
if exist "%~1" (
    echo ERROR: Failed to remove file "%~1".
    set "CLEAN_FAILED=1"
)
exit /b 0

:Usage
echo Usage: %~nx0 [--dry-run]
exit /b 2
