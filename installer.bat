@echo off
setlocal enabledelayedexpansion
title Signal Safety Menu Installer
color 1F
cls
echo.
echo   ======================================
echo      Signal Safety Menu - Installer
echo   ======================================
echo.

title Signal Safety Menu Installer // Finding game...

set "STEAM_PATH="
for /f "tokens=2*" %%a in ('reg query "HKLM\SOFTWARE\WOW6432Node\Valve\Steam" /v InstallPath 2^>nul') do set "STEAM_PATH=%%b"
if not defined STEAM_PATH (
    for /f "tokens=2*" %%a in ('reg query "HKLM\SOFTWARE\Valve\Steam" /v InstallPath 2^>nul') do set "STEAM_PATH=%%b"
)

if not defined STEAM_PATH if exist "C:\Program Files (x86)\Steam" set "STEAM_PATH=C:\Program Files (x86)\Steam"
if not defined STEAM_PATH if exist "C:\Program Files\Steam" set "STEAM_PATH=C:\Program Files\Steam"
if not defined STEAM_PATH if exist "D:\Steam" set "STEAM_PATH=D:\Steam"
if not defined STEAM_PATH if exist "D:\SteamLibrary" set "STEAM_PATH=D:\SteamLibrary"

set "GAME_PATH="

if defined STEAM_PATH (
    if exist "%STEAM_PATH%\steamapps\common\Gorilla Tag\Gorilla Tag_Data" (
        set "GAME_PATH=%STEAM_PATH%\steamapps\common\Gorilla Tag"
    )
)

if not defined GAME_PATH if defined STEAM_PATH (
    if exist "%STEAM_PATH%\steamapps\libraryfolders.vdf" (
        for /f "tokens=*" %%L in ('findstr /C:"path" "%STEAM_PATH%\steamapps\libraryfolders.vdf"') do (
            for /f "tokens=4 delims=^"" %%P in ("%%L") do (
                if exist "%%P\steamapps\common\Gorilla Tag\Gorilla Tag_Data" (
                    set "GAME_PATH=%%P\steamapps\common\Gorilla Tag"
                )
            )
        )
    )
)

if not defined GAME_PATH if exist "C:\Program Files\Oculus\Software\Software\another-axiom-gorilla-tag" set "GAME_PATH=C:\Program Files\Oculus\Software\Software\another-axiom-gorilla-tag"
if not defined GAME_PATH if exist "E:\SteamLibrary\steamapps\common\Gorilla Tag\Gorilla Tag_Data" set "GAME_PATH=E:\SteamLibrary\steamapps\common\Gorilla Tag"
if not defined GAME_PATH if exist "E:\Steam\steamapps\common\Gorilla Tag\Gorilla Tag_Data" set "GAME_PATH=E:\Steam\steamapps\common\Gorilla Tag"

if not defined GAME_PATH (
    color 4F
    echo   [x] Could not find Gorilla Tag.
    echo       Make sure the game is installed through Steam or Oculus.
    echo.
    pause
    exit /b 1
)

echo   [+] Found: %GAME_PATH%

title Signal Safety Menu Installer // Checking BepInEx...

if not exist "%GAME_PATH%\BepInEx" (
    echo   [!] BepInEx not found - installing automatically...
    echo.

    title Signal Safety Menu Installer // Downloading BepInEx...

    echo   [2] Downloading BepInEx...
    powershell -NoProfile -Command ^
        "[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12; " ^
        "$r = Invoke-RestMethod 'https://api.github.com/repos/BepInEx/BepInEx/releases/latest' -Headers @{'User-Agent'='SignalInstaller'}; " ^
        "$a = $r.assets | Where-Object { $_.name -like 'BepInEx_win_x64*' -and $_.name -like '*.zip' } | Select-Object -First 1; " ^
        "if (-not $a) { Write-Host '  [x] Could not find BepInEx download'; exit 1 }; " ^
        "Write-Host ('  [+] ' + $a.name); " ^
        "$wc = New-Object Net.WebClient; $wc.Headers.Add('User-Agent','SignalInstaller'); " ^
        "$wc.DownloadFile($a.browser_download_url, '%TEMP%\BepInEx_install.zip'); " ^
        "Write-Host '  [+] Downloaded'"
    if errorlevel 1 (
        color 4F
        echo.
        echo   [x] BepInEx installation failed.
        echo       Try downloading manually from github.com/BepInEx/BepInEx/releases
        echo.
        pause
        exit /b 1
    )

    echo   [3] Extracting BepInEx...
    powershell -NoProfile -Command "Expand-Archive -Path '%TEMP%\BepInEx_install.zip' -DestinationPath '%GAME_PATH%' -Force"
    del "%TEMP%\BepInEx_install.zip" >nul 2>&1

    if not exist "%GAME_PATH%\BepInEx" (
        color 4F
        echo.
        echo   [x] BepInEx installation failed.
        echo       Try downloading manually from github.com/BepInEx/BepInEx/releases
        echo.
        pause
        exit /b 1
    )

    echo   [+] BepInEx installed!
    echo.
) else (
    echo   [+] BepInEx found.
)

set "PLUGINS=%GAME_PATH%\BepInEx\plugins"
if not exist "%PLUGINS%" mkdir "%PLUGINS%"

title Signal Safety Menu Installer // Downloading menu...

echo   [4] Downloading Signal Safety Menu...
set "DLL_PATH=%PLUGINS%\SignalSafetyMenu.dll"
set "DLL_URL=https://github.com/mojhehh/Signal-Menu/releases/latest/download/SignalSafetyMenu.dll"

powershell -NoProfile -Command ^
    "[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12; " ^
    "(New-Object Net.WebClient).DownloadFile('%DLL_URL%', '%DLL_PATH%')"

if not exist "%DLL_PATH%" (
    color 4F
    echo.
    echo   [x] Download failed. Check your internet connection.
    del "%DLL_PATH%" >nul 2>&1
    echo.
    pause
    exit /b 1
)
for %%A in ("%DLL_PATH%") do if %%~zA LSS 1024 (
    color 4F
    echo.
    echo   [x] Download failed. Check your internet connection.
    del "%DLL_PATH%" >nul 2>&1
    echo.
    pause
    exit /b 1
)
echo   [+] Signal Safety Menu downloaded.

title Signal Safety Menu Installer // Downloading updater...

echo   [5] Downloading Auto Updater...
set "UPD_PATH=%PLUGINS%\SignalAutoUpdater.dll"
set "UPD_URL=https://github.com/mojhehh/Signal-Menu/releases/latest/download/SignalAutoUpdater.dll"

powershell -NoProfile -Command ^
    "[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12; " ^
    "(New-Object Net.WebClient).DownloadFile('%UPD_URL%', '%UPD_PATH%')"

if exist "%UPD_PATH%" (
    echo   [+] Auto Updater downloaded.
) else (
    echo   [!] Auto Updater download failed (non-critical^).
)

title Signal Safety Menu Installer // Done!
color 2F
echo.
echo   ======================================
echo      Installation complete!
echo.
echo      Installed to:
echo      %PLUGINS%
echo.
echo      Launch Gorilla Tag to load the mod.
echo      The mod auto-updates on each launch.
echo   ======================================
echo.
pause
