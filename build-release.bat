@echo off
setlocal enabledelayedexpansion

echo ========================================================
echo       Windows Junction Manager - Release Builder
echo ========================================================
echo.

cd /d "%~dp0"

:: 1. Read current version from JunctionManager.csproj
set "CSPROJ=JunctionManagerWpf\JunctionManager.csproj"
set "ABOUT_XAML=JunctionManagerWpf\AboutWindow.xaml"

for /f "tokens=3 delims=<>" %%a in ('findstr /i "<Version>" "%CSPROJ%"') do (
    set "CURRENT_VER=%%a"
)

if "%CURRENT_VER%"=="" (
    set "CURRENT_VER=2.1.0"
)

:: 2. Check if version was provided as argument, otherwise prompt
set "VERSION=%~1"
if "%VERSION%"=="" (
    echo Current version in csproj: %CURRENT_VER%
    set /p "INPUT_VER=Enter release version [press Enter for %CURRENT_VER%]: "
    if "!INPUT_VER!"=="" (
        set "VERSION=%CURRENT_VER%"
    ) else (
        set "VERSION=!INPUT_VER!"
    )
)

:: Strip leading 'v' if entered by user (e.g. v2.1.0 -> 2.1.0)
if "!VERSION:~0,1!"=="v" set "VERSION=!VERSION:~1!"
if "!VERSION:~0,1!"=="V" set "VERSION=!VERSION:~1!"

echo.
echo [*] Building Release for Version: v!VERSION!
echo.

:: 3. Update version in csproj and AboutWindow if changed
if not "!VERSION!"=="%CURRENT_VER%" (
    echo [*] Updating version in %CSPROJ% and %ABOUT_XAML%...
    powershell -NoProfile -Command "(Get-Content '%CSPROJ%') -replace '<Version>.*?</Version>', '<Version>!VERSION!</Version>' | Set-Content '%CSPROJ%'"
    powershell -NoProfile -Command "(Get-Content '%ABOUT_XAML%') -replace 'Text=\"v.*?\"', 'Text=\"v!VERSION!\"' | Set-Content '%ABOUT_XAML%'"
)

:: 4. Create / clean dist directory
if not exist "dist" mkdir "dist"

:: 5. Publish single-file binary with dotnet
echo [*] Publishing single-file executable...
dotnet publish "%CSPROJ%" -c Release -r win-x64 -p:PublishSingleFile=true -p:Version=!VERSION! --self-contained false -o dist

if %ERRORLEVEL% neq 0 (
    echo.
    echo [ERROR] Build failed with exit code %ERRORLEVEL%.
    pause
    exit /b %ERRORLEVEL%
)

:: 6. Copy version-appended binary
set "VERSIONED_EXE=dist\JunctionManager-v!VERSION!-win-x64.exe"
copy /y "dist\JunctionManager.exe" "!VERSIONED_EXE!" >nul

echo.
echo ========================================================
echo [SUCCESS] Release build completed!
echo - Standard:  dist\JunctionManager.exe
echo - Versioned: !VERSIONED_EXE!
echo ========================================================
echo.

:: 7. GitHub Release Integration
echo GitHub Releases does not automatically link local files without a Git tag or GitHub API release.
echo.
set /p "DO_GIT=Do you want to create and push git tag 'v!VERSION!' to trigger GitHub Release? [Y/N]: "

if /i "!DO_GIT!"=="Y" (
    echo.
    echo [*] Checking git status...
    git add "%CSPROJ%" "%ABOUT_XAML%"
    git commit -m "Bump version to v!VERSION!" >nul 2>&1
    
    echo [*] Creating tag v!VERSION!...
    git tag -a "v!VERSION!" -m "Release v!VERSION!" -f
    
    echo [*] Pushing tag to GitHub origin...
    git push origin "v!VERSION!"
    
    if %ERRORLEVEL% equ 0 (
        echo.
        echo [OK] Tag v!VERSION! pushed successfully!
        echo GitHub Actions will now automatically build and publish the release at:
        echo https://github.com/binodkhandual/Windows-Junction-Manager/releases
    ) else (
        echo.
        echo [WARNING] Failed to push tag. Ensure your git remote is configured and you have push access.
    )
) else (
    echo.
    echo To publish this release manually:
    echo 1. Go to: https://github.com/binodkhandual/Windows-Junction-Manager/releases/new
    echo 2. Tag version: v!VERSION!
    echo 3. Drag and drop '!VERSIONED_EXE!' into the release assets.
)

:: 8. Check if GitHub CLI is installed for direct upload
where gh >nul 2>&1
if %ERRORLEVEL% equ 0 (
    echo.
    set /p "DO_GH=GitHub CLI (gh) detected. Would you like to create the release directly via 'gh'? [Y/N]: "
    if /i "!DO_GH!"=="Y" (
        gh release create "v!VERSION!" "!VERSIONED_EXE!" "dist\JunctionManager.exe" --title "Junction Manager v!VERSION!" --generate-notes
    )
)

echo.
echo Opening dist folder...
start "" "dist"

echo Done.
