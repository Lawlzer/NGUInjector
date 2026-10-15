param(
    [switch]$IncludeInjector,
    [switch]$BrowserTests,
    [string]$GameFolder = $env:NGU_IDLE_DIR
)

$ErrorActionPreference = 'Stop'
$UiDir = Join-Path $PSScriptRoot 'NGUInjector.UI'
if (-not (Test-Path -LiteralPath $UiDir -PathType Container)) { throw "UI project not found: $UiDir" }

Push-Location -LiteralPath $UiDir
try {
    if (-not (Test-Path -LiteralPath 'node_modules')) {
        & npm ci
        if ($LASTEXITCODE -ne 0) { throw 'npm ci failed.' }
    }
    & npm run check
    if ($LASTEXITCODE -ne 0) { throw 'Svelte/TypeScript checks failed.' }
    & npm test
    if ($LASTEXITCODE -ne 0) { throw 'Frontend unit tests failed.' }
    & npm run build
    if ($LASTEXITCODE -ne 0) { throw 'Frontend build failed.' }
    & cargo fmt --manifest-path 'src-tauri/Cargo.toml' --all -- --check
    if ($LASTEXITCODE -ne 0) { throw 'Rust formatting check failed.' }
    & cargo test --manifest-path 'src-tauri/Cargo.toml' --locked
    if ($LASTEXITCODE -ne 0) { throw 'Rust tests failed.' }
    & cargo check --manifest-path 'src-tauri/Cargo.toml' --locked
    if ($LASTEXITCODE -ne 0) { throw 'Rust compile check failed.' }
    if ($BrowserTests) {
        & npm run test:ui
        if ($LASTEXITCODE -ne 0) { throw 'Browser tests failed.' }
    }
} finally {
    Pop-Location
}

& dotnet run --project (Join-Path $PSScriptRoot 'checks\ExternalUi\ExternalUiChecks.csproj')
if ($LASTEXITCODE -ne 0) { throw 'C# external UI bridge checks failed.' }
& dotnet run --project (Join-Path $PSScriptRoot 'checks\SustainedFocus\SustainedFocusChecks.csproj')
if ($LASTEXITCODE -ne 0) { throw 'Sustained Quick/NGU checks failed.' }

if ($IncludeInjector) {
    if ([string]::IsNullOrWhiteSpace($GameFolder)) { throw 'Pass -GameFolder or set NGU_IDLE_DIR for the injector build.' }
    $MsBuild = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe'
    if (-not (Test-Path -LiteralPath $MsBuild -PathType Leaf)) { throw "MSBuild not found: $MsBuild" }
    & $MsBuild (Join-Path $PSScriptRoot 'NGUInjector.sln') /t:Build /p:Configuration=Release "/p:GameFolder=$GameFolder" /m /verbosity:minimal
    if ($LASTEXITCODE -ne 0) { throw 'Injector build failed.' }
    & $MsBuild (Join-Path $PSScriptRoot 'checks\BlackBarChecks.csproj') /t:Build /verbosity:minimal
    if ($LASTEXITCODE -ne 0) { throw 'Black-bar checks build failed.' }
    & (Join-Path $PSScriptRoot 'checks\bin\BlackBarChecks.exe') (Join-Path $PSScriptRoot 'NGUInjector\bin\Release\NGUInjector.dll') (Join-Path $GameFolder 'NGUIdle_Data\Managed')
    if ($LASTEXITCODE -ne 0) { throw 'Black-bar/scheduler checks failed.' }
}

'UI verification passed. No live game actions were performed.'
