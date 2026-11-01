@echo off
setlocal
cd /d "%~dp0"
set "BUILD_NO_PAUSE="
if /i "%~1"=="--no-pause" set "BUILD_NO_PAUSE=1"
set "BUILD_PROJECT=PaperBatchDownloader\PaperBatchDownloader.csproj"
set "BUILD_LOG_DIR=%~dp0build-logs"
set "BUILD_LOG=%BUILD_LOG_DIR%\build_release.log"
set "BUILD_LOG_READY="

if not exist "%BUILD_PROJECT%" (
  echo ERROR: Source project was not found.
  echo Keep this BAT beside the PaperBatchDownloader source folder.
  goto :fail
)
where dotnet >nul 2>nul
if errorlevel 1 (
  echo ERROR: dotnet was not found. Install the .NET 8 SDK for Windows x64.
  echo Then reopen this folder and run this BAT again.
  goto :fail
)
dotnet --list-sdks | findstr /b /c:"8." >nul
if errorlevel 1 (
  echo ERROR: The .NET 8 SDK is required. Installing only the runtime is not enough.
  goto :fail
)
if not exist "%BUILD_LOG_DIR%" mkdir "%BUILD_LOG_DIR%"
if not exist "%BUILD_LOG_DIR%" goto :fail
> "%BUILD_LOG%" echo Paper Batch Downloader - local Windows x64 build
if errorlevel 1 goto :fail
set "BUILD_LOG_READY=1"

echo [1/3] Reading version from the source project...
set "BUILD_VERSION="
set "BUILD_ASSEMBLY="
for /f "delims=" %%V in ('dotnet msbuild "%BUILD_PROJECT%" -nologo -getProperty:Version 2^>nul') do set "BUILD_VERSION=%%V"
for /f "delims=" %%A in ('dotnet msbuild "%BUILD_PROJECT%" -nologo -getProperty:AssemblyName 2^>nul') do set "BUILD_ASSEMBLY=%%A"
if not defined BUILD_VERSION goto :fail
if not defined BUILD_ASSEMBLY goto :fail
set "BUILD_OUTPUT=%~dp0build-output\PaperBatchDownloader-v%BUILD_VERSION%-win-x64"
set "BUILD_EXE=%BUILD_OUTPUT%\%BUILD_ASSEMBLY%.exe"
echo Version: %BUILD_VERSION%
echo Output: "%BUILD_OUTPUT%"
echo.

echo [2/3] Restoring packages. Internet access may be required...
dotnet restore "%BUILD_PROJECT%" -r win-x64 >> "%BUILD_LOG%" 2>&1
if errorlevel 1 goto :fail

echo [3/3] Building the self-contained Windows x64 executable...
dotnet publish "%BUILD_PROJECT%" -c Release -r win-x64 --self-contained true --no-restore -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o "%BUILD_OUTPUT%" >> "%BUILD_LOG%" 2>&1
if errorlevel 1 goto :fail
if not exist "%BUILD_EXE%" goto :fail

echo.
echo BUILD SUCCEEDED.
echo EXE: "%BUILD_EXE%"
echo LOG: "%BUILD_LOG%"
echo You can open the EXE above. No .NET runtime install is needed to run this build.
if not defined BUILD_NO_PAUSE pause
exit /b 0

:fail
echo.
echo BUILD FAILED. No successful build was reported.
if defined BUILD_LOG_READY (
  echo Review the error details below and in "%BUILD_LOG%":
  type "%BUILD_LOG%"
)
if not defined BUILD_NO_PAUSE pause
exit /b 1
