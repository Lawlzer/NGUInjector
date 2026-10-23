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
        & npm run test:ui -- --workers=2
        if ($LASTEXITCODE -ne 0) { throw 'Browser tests failed.' }
    }
} finally {
    Pop-Location
}

& dotnet run --project (Join-Path $PSScriptRoot 'checks\ExternalUi\ExternalUiChecks.csproj')
if ($LASTEXITCODE -ne 0) { throw 'C# external UI bridge checks failed.' }
& dotnet run --project (Join-Path $PSScriptRoot 'checks\PresetLibrary\PresetLibraryChecks.csproj') --configuration Release
if ($LASTEXITCODE -ne 0) { throw 'Global preset library checks failed.' }
& dotnet run --project (Join-Path $PSScriptRoot 'checks\Combat\CombatChecks.csproj')
if ($LASTEXITCODE -ne 0) { throw 'Adventure/combat routing checks failed.' }
& dotnet run --project (Join-Path $PSScriptRoot 'checks\SustainedFocus\SustainedFocusChecks.csproj')
if ($LASTEXITCODE -ne 0) { throw 'Sustained Quick/NGU checks failed.' }
& dotnet run --project (Join-Path $PSScriptRoot 'checks\SustainedFocus\SustainedFocusChecks.csproj') --configuration Release -- --execution-core
if ($LASTEXITCODE -ne 0) { throw 'Execution-core source authority checks failed.' }
& dotnet run --project (Join-Path $PSScriptRoot 'checks\ProtectedTiming\ProtectedTimingChecks.csproj')
if ($LASTEXITCODE -ne 0) { throw 'Protected/flexible stage timing checks failed.' }
& dotnet run --configuration Release --project (Join-Path $PSScriptRoot 'checks\CombatPrep\CombatPrepChecks.csproj')
if ($LASTEXITCODE -ne 0) { throw 'Combat prep projection checks failed.' }
& dotnet run --project (Join-Path $PSScriptRoot 'checks\Presets\PresetsChecks.csproj') -c Release
if ($LASTEXITCODE -ne 0) { throw 'Weighted preset numeric checks failed.' }
foreach ($project in @('ActivityPolicy\ActivityPolicyChecks', 'ActivityPolicy\IntegrationChecks', 'SharedSupport\SharedSupportChecks', 'Support\SupportChecks', 'Support\BeardReceiptAvailabilityChecks', 'Beards\BeardChecks')) {
    & dotnet run --configuration Release --project (Join-Path $PSScriptRoot "checks\$project.csproj")
    if ($LASTEXITCODE -ne 0) { throw "Shared activity/support checks failed: $project" }
}
foreach ($project in @('AutoExp\AutoExpChecks', 'PresetIntegration\NumericIntegrationChecks', 'ResourceWriter\ResourceWriterChecks', 'FiniteActions\FiniteActionsChecks')) {
    & dotnet run --configuration Release --project (Join-Path $PSScriptRoot "checks\$project.csproj")
    if ($LASTEXITCODE -ne 0) { throw "Execution cutover source checks failed: $project" }
}
& dotnet run --configuration Release --project (Join-Path $PSScriptRoot 'checks\RouteExecutorSyntax\RouteExecutorSyntaxChecks.csproj') -- (Join-Path $PSScriptRoot 'NGUInjector')
if ($LASTEXITCODE -ne 0) { throw 'Route executor syntax/writer-inventory checks failed.' }

if ($IncludeInjector) {
    if ([string]::IsNullOrWhiteSpace($GameFolder)) { throw 'Pass -GameFolder or set NGU_IDLE_DIR for the injector build.' }
    & (Join-Path $PSScriptRoot 'checks\VerifyExecutorOffline.ps1') -GameFolder $GameFolder
}

'UI verification passed. No live game actions were performed.'
