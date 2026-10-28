function Invoke-UiVerification($Context, [string]$Repo) {
    $ErrorActionPreference = 'Stop'
    $ui = Join-Path ([IO.Path]::GetFullPath($Repo)) 'NGUInjector.UI'
    if (-not (Test-Path -LiteralPath $ui -PathType Container)) { throw "UI project not found: $ui" }
    $workers = 2
    if ($null -ne $Context.BrowserWorkers) { $workers = [int]$Context.BrowserWorkers }
    if ($workers -lt 1) { throw 'BrowserWorkers must be positive.' }
    $failures = [Collections.Generic.List[string]]::new()
    Push-Location -LiteralPath $ui
    try {
        $npmReady = Test-Path -LiteralPath 'node_modules' -PathType Container
        if (-not $npmReady) {
            try { Invoke-TestAllNative $Context 'ui.dependencies' 'npm' @('ci'); $npmReady = $true }
            catch { $failures.Add('dependencies') }
        }
        if ($npmReady) {
            foreach ($gate in @('check', 'test', 'build')) {
                try { Invoke-TestAllNative $Context "ui.$gate" 'npm' @('run', $gate) }
                catch { $failures.Add($gate) }
            }
        }
        try { Invoke-TestAllNative $Context 'ui.cargo.fmt' 'cargo' @('fmt', '--manifest-path', 'src-tauri/Cargo.toml', '--all', '--', '--check') }
        catch { $failures.Add('cargo fmt') }
        foreach ($gate in @('test', 'check')) {
            try { Invoke-TestAllNative $Context "ui.cargo.$gate" 'cargo' @($gate, '--manifest-path', 'src-tauri/Cargo.toml', '--locked') }
            catch { $failures.Add("cargo $gate") }
        }
        if ($npmReady) {
            try { Invoke-TestAllNative $Context 'ui.browser' 'npm' @('run', 'test:ui', '--', "--workers=$workers") }
            catch { $failures.Add('browser') }
        }
    } finally { Pop-Location }
    if ($failures.Count) { throw "UI verification failed: $($failures -join ', ')." }
}

function Invoke-SourceVerification($Context, [string]$Repo) {
    $ErrorActionPreference = 'Stop'
    $Repo = [IO.Path]::GetFullPath($Repo)
    $projects = @(
        'ExternalUi\ExternalUiChecks', 'PresetLibrary\PresetLibraryChecks', 'Combat\CombatChecks',
        'SustainedFocus\SustainedFocusChecks', 'ProtectedTiming\ProtectedTimingChecks', 'CombatPrep\CombatPrepChecks',
        'Presets\PresetsChecks', 'ActivityPolicy\ActivityPolicyChecks', 'ActivityPolicy\IntegrationChecks',
        'SharedSupport\SharedSupportChecks', 'Support\SupportChecks', 'Support\BeardReceiptAvailabilityChecks',
        'Beards\BeardChecks', 'AutoExp\AutoExpChecks', 'PresetIntegration\NumericIntegrationChecks',
        'ResourceWriter\ResourceWriterChecks', 'FiniteActions\FiniteActionsChecks', 'ActionSessions\PrepSessionChecks',
        'ActionSessions\BloodSessionChecks', 'SaveSafety\BundleChecks'
    )
    $failures = [Collections.Generic.List[string]]::new()
    Push-Location -LiteralPath $Repo
    try {
        foreach ($project in $projects) {
            $name = 'source.' + $project.Replace('\', '.')
            try { Invoke-TestAllNative $Context $name 'dotnet' @('run', '--project', (Join-Path $Repo "checks\$project.csproj"), '--configuration', 'Release') }
            catch { $failures.Add($project) }
        }
        # This entry reaches execution-core checks even if an earlier full-suite check fails.
        try {
            Invoke-TestAllNative $Context 'source.SustainedFocus.execution-core' 'dotnet' @(
                'run', '--project', (Join-Path $Repo 'checks\SustainedFocus\SustainedFocusChecks.csproj'),
                '--configuration', 'Release', '--', '--execution-core'
            )
        } catch { $failures.Add('SustainedFocus --execution-core') }
        try {
            Invoke-TestAllNative $Context 'source.RouteExecutorSyntax' 'dotnet' @(
                'run', '--project', (Join-Path $Repo 'checks\RouteExecutorSyntax\RouteExecutorSyntaxChecks.csproj'),
                '--configuration', 'Release', '--', (Join-Path $Repo 'NGUInjector')
            )
        } catch { $failures.Add('RouteExecutorSyntax') }
    } finally { Pop-Location }
    if ($failures.Count) { throw "Source verification failed: $($failures -join ', ')." }
}
