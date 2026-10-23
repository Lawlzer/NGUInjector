param(
    [string]$GameFolder = $env:NGU_IDLE_DIR,
    [string]$Filter = '',
    [string]$LogPath = ''
)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$selectors = @()
if ($Filter) { $selectors += $Filter }
& (Join-Path $repo 'checks\VerifyExecutorOffline.ps1') -Suite AutoExp -Selector $selectors -GameFolder $GameFolder -LogPath $LogPath
