param(
    [switch]$Dev,
    [switch]$Build
)

$ErrorActionPreference = 'Stop'
$UiDir = Join-Path $PSScriptRoot 'NGUInjector.UI'
$Executable = Join-Path $UiDir 'src-tauri\target\release\nguinjector-ui.exe'

if (-not (Test-Path -LiteralPath $UiDir -PathType Container)) {
    throw "UI project not found: $UiDir"
}

if ($Dev -or $Build -or -not (Test-Path -LiteralPath $Executable)) {
    Push-Location -LiteralPath $UiDir
    try {
        if (-not (Test-Path -LiteralPath 'node_modules')) {
            & npm ci
            if ($LASTEXITCODE -ne 0) { throw 'UI dependency installation failed.' }
        }
        if ($Dev) {
            & npm run tauri -- dev
            if ($LASTEXITCODE -ne 0) { throw 'Tauri development session failed.' }
            return
        }
        & npm run tauri -- build --no-bundle
        if ($LASTEXITCODE -ne 0) { throw 'Tauri build failed.' }
    } finally {
        Pop-Location
    }
}

# Opening the controller never restarts the game, injects a DLL, or runs an automation action.
Start-Process -FilePath $Executable -WorkingDirectory $UiDir
