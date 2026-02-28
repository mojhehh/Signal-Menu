@echo off
if "%~1"=="RUN" goto :start
cmd /k "%~f0" RUN
exit /b

:start
title Signal Safety Menu Installer
color 1F
cls
echo.
echo   ======================================
echo      Signal Safety Menu - Installer
echo   ======================================
echo.

set "PS=%TEMP%\signal_install.ps1"
powershell -NoProfile -Command "$s=$false; foreach($l in (Get-Content '%~f0')){ if($s){$l} elseif($l -eq '#__PSSTART'){$s=$true} }" > "%PS%"
powershell -NoProfile -ExecutionPolicy Bypass -File "%PS%"
if errorlevel 1 (color 4F) else (color 2F)
del "%PS%" >nul 2>&1
echo.
echo   Press any key to exit...
pause >nul
exit /b

#__PSSTART
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$ErrorActionPreference = 'Stop'

function Find-GorillaTag {
    $steamReg = $null
    try { $steamReg = (Get-ItemProperty 'HKLM:\SOFTWARE\WOW6432Node\Valve\Steam' -EA Stop).InstallPath } catch {}
    if (-not $steamReg) { try { $steamReg = (Get-ItemProperty 'HKLM:\SOFTWARE\Valve\Steam' -EA Stop).InstallPath } catch {} }

    $roots = @()
    if ($steamReg) { $roots += $steamReg }
    $roots += 'C:\Program Files (x86)\Steam','C:\Program Files\Steam','D:\Steam','D:\SteamLibrary','E:\Steam','E:\SteamLibrary'

    foreach ($root in $roots) {
        $g = Join-Path $root 'steamapps\common\Gorilla Tag'
        if (Test-Path (Join-Path $g 'Gorilla Tag_Data')) { return $g }
    }

    if ($steamReg) {
        $vdf = Join-Path $steamReg 'steamapps\libraryfolders.vdf'
        if (Test-Path $vdf) {
            $content = Get-Content $vdf -Raw
            $ms = [regex]::Matches($content, '"path"\s+"([^"]+)"')
            foreach ($m in $ms) {
                $g = Join-Path $m.Groups[1].Value 'steamapps\common\Gorilla Tag'
                if (Test-Path (Join-Path $g 'Gorilla Tag_Data')) { return $g }
            }
        }
    }

    $oculus = 'C:\Program Files\Oculus\Software\Software\another-axiom-gorilla-tag'
    if (Test-Path $oculus) { return $oculus }
    return $null
}

$gamePath = Find-GorillaTag
if (-not $gamePath) {
    Write-Host '  [x] Could not find Gorilla Tag.' -ForegroundColor Red
    Write-Host '      Make sure the game is installed via Steam or Oculus.'
    exit 1
}
Write-Host "  [+] Found: $gamePath"

$bepPath = Join-Path $gamePath 'BepInEx'
if (-not (Test-Path $bepPath)) {
    Write-Host '  [!] BepInEx not found - installing...'
    Write-Host '  [2] Downloading BepInEx...'
    $r = Invoke-RestMethod 'https://api.github.com/repos/BepInEx/BepInEx/releases/latest' -Headers @{'User-Agent'='SignalInstaller'}
    $a = $r.assets | Where-Object { $_.name -like 'BepInEx_win_x64*' -and $_.name -like '*.zip' } | Select-Object -First 1
    if (-not $a) { Write-Host '  [x] Could not find BepInEx download' -ForegroundColor Red; exit 1 }
    $zip = Join-Path $env:TEMP 'BepInEx_install.zip'
    (New-Object Net.WebClient).DownloadFile($a.browser_download_url, $zip)
    Write-Host '  [3] Extracting BepInEx...'
    Expand-Archive -Path $zip -DestinationPath $gamePath -Force
    Remove-Item $zip -EA SilentlyContinue
    if (-not (Test-Path $bepPath)) { Write-Host '  [x] BepInEx install failed' -ForegroundColor Red; exit 1 }
    Write-Host '  [+] BepInEx installed!'
} else {
    Write-Host '  [+] BepInEx found.'
}

$plugins = Join-Path $bepPath 'plugins'
if (-not (Test-Path $plugins)) { $null = New-Item $plugins -ItemType Directory -Force }

$repo = 'https://github.com/mojhehh/Signal-Menu/releases/latest/download'
$wc = New-Object Net.WebClient

Write-Host '  [4] Downloading Signal Safety Menu...'
$dll = Join-Path $plugins 'SignalSafetyMenu.dll'
$wc.DownloadFile("$repo/SignalSafetyMenu.dll", $dll)
if (-not (Test-Path $dll) -or (Get-Item $dll).Length -lt 1024) {
    Write-Host '  [x] Download failed. Check your internet.' -ForegroundColor Red
    exit 1
}
Write-Host '  [+] Signal Safety Menu downloaded.'

Write-Host '  [5] Downloading Auto Updater...'
$upd = Join-Path $plugins 'SignalAutoUpdater.dll'
try {
    $wc.DownloadFile("$repo/SignalAutoUpdater.dll", $upd)
    Write-Host '  [+] Auto Updater downloaded.'
} catch {
    Write-Host '  [!] Auto Updater download failed - not critical.'
}

Write-Host ''
Write-Host '  ======================================'
Write-Host '     Installation complete!'
Write-Host ''
Write-Host "     Installed to: $plugins"
Write-Host ''
Write-Host '     Launch Gorilla Tag to load the mod.'
Write-Host '     The mod auto-updates on each launch.'
Write-Host '  ======================================'
exit 0
