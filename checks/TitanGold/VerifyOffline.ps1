param()
$ErrorActionPreference = 'Stop'
$repo = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$msbuild = 'C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe'
$game = 'C:\Gaming\Launchers\Steam\Steam\steamapps\common\NGU IDLE'
$managed = Join-Path $game 'NGUIdle_Data\Managed'
if (!(Test-Path -LiteralPath (Join-Path $repo 'NGUInjector')) -or !(Test-Path -LiteralPath $managed)) { throw 'Offline verification parents are unavailable.' }
$mutex = [Threading.Mutex]::new($false, 'Local\NGUInjector-Executor-Offline-Verification')
$held = $false
try {
    try { $held = $mutex.WaitOne([TimeSpan]::FromMinutes(30)) }
    catch [Threading.AbandonedMutexException] { $held = $true }
    if (!$held) { throw 'Another offline verification is still active.' }
    & $msbuild (Join-Path $repo 'NGUInjector.sln') /restore /t:Rebuild /p:Configuration=Release "/p:GameFolder=$game" /v:minimal /nologo
    if ($LASTEXITCODE -ne 0) { throw 'Production rebuild failed.' }
    & $msbuild (Join-Path $repo 'checks\TitanGold\TitanGoldChecks.csproj') /restore /t:Rebuild /p:Configuration=Release /v:minimal /nologo
    if ($LASTEXITCODE -ne 0) { throw 'TitanGold fixture rebuild failed.' }
    & (Join-Path $repo 'checks\TitanGold\bin\TitanGoldChecks.exe') (Join-Path $repo 'NGUInjector\bin\Release\NGUInjector.dll') $managed
    if ($LASTEXITCODE -ne 0) { throw 'TitanGold failed.' }
} finally {
    if ($held) { $mutex.ReleaseMutex() }
    $mutex.Dispose()
}
