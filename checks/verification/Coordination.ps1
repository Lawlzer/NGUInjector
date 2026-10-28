function Get-VerificationSourceStamp([string]$Repo, [string]$GameFolder = '') {
    $PSNativeCommandUseErrorActionPreference = $false
    $paths = @(& git -C $Repo -c core.quotepath=false ls-files --cached --others --exclude-standard -z)
    if ($LASTEXITCODE -ne 0) { throw 'Cannot enumerate verification inputs with git.' }
    $files = @((($paths -join "`n").Split([char]0, [StringSplitOptions]::RemoveEmptyEntries)) |
        Where-Object {
            $_ -notmatch '(^|/)(bin|obj|node_modules|target|artifacts|test-results[^/]*|playwright-report)(/|$)' -and
            $_ -match '\.(cs|csproj|proj|props|targets|sln|resx|json|jsonc|ts|tsx|js|cjs|mjs|svelte|html|css|rs|toml|lock|config|ps1|bat|txt|csv|dll|exe|png|svg)$'
        } | Sort-Object -Unique)
    if (-not $files.Count) { throw 'No verification inputs were found; source identity is unavailable.' }
    $hash = [Security.Cryptography.IncrementalHash]::CreateHash([Security.Cryptography.HashAlgorithmName]::SHA256)
    try {
        foreach ($path in $files) {
            $full = Join-Path $Repo $path
            $content = if ([IO.File]::Exists($full)) { (Get-FileHash -LiteralPath $full -Algorithm SHA256 -ErrorAction Stop).Hash } else { 'DELETED' }
            $hash.AppendData([Text.Encoding]::UTF8.GetBytes($path + "`0" + $content + "`n"))
        }
        if ($GameFolder) {
            $managed = Join-Path $GameFolder 'NGUIdle_Data/Managed'
            if ([IO.Directory]::Exists($managed)) {
                foreach ($path in @([IO.Directory]::EnumerateFiles($managed, '*.dll') | Sort-Object)) {
                    $hash.AppendData([Text.Encoding]::UTF8.GetBytes($path + "`0" + (Get-FileHash -LiteralPath $path -Algorithm SHA256 -ErrorAction Stop).Hash + "`n"))
                }
            }
        }
        return [Convert]::ToHexString($hash.GetHashAndReset())
    } finally { $hash.Dispose() }
}

function Write-VerificationHandoff($Context, [string]$Reason, [string]$Note = '') {
    $report = $Context.Report
    $path = Join-Path $Context.Repo 'artifacts/agent-handoff.md'
    $entry = @(
        "`n## $([datetimeoffset]::UtcNow.ToString('o')) | $($report.id) | verification handoff",
        "- Request/scope: $($report.scope); suites=$($report.request.suites -join ','); selectors=$($report.request.selectors -join ',').",
        "- Repository: $($Context.Repo)",
        "- Blocker: $Reason",
        "- Owner: $($report.owner | ConvertTo-Json -Compress -Depth 5)",
        "- Command: $($report.request.command)",
        "- Source identity: before=$($report.sourceBefore); after=$($report.sourceAfter). Endpoint checks are not an immutable snapshot.",
        "- Artifact: $($Context.NativeArtifact | ConvertTo-Json -Compress -Depth 5)",
        "- Results/first failures: $($report.errors | ConvertTo-Json -Compress -Depth 10)",
        "- Logs/report: $(Join-Path $Context.ReportDir 'report.json'); phase logs in $($Context.ReportDir).",
        '- Pending checks/next steps: coordinate the shared files/outputs, read every open handoff entry, then run the requested scope on stable source. Record results and resolve entries without deleting their evidence.',
        "- Agent context, changed files, decisions and required continuation information: $Note",
        '- Safety: offline verification only. No deployment, injection, game commands, save changes or commit authorized. Deferred work is not a pass.'
    ) -join [Environment]::NewLine
    $report.handoff = @{ path = $path; entry = $entry; written = $false; error = $null }
    Save-TestAllReport $Context
    $stream = $null
    try {
        $parent = Split-Path -Parent $path
        if (-not (Test-Path -LiteralPath $Context.Repo -PathType Container)) { throw 'Repository parent is unavailable.' }
        $null = [IO.Directory]::CreateDirectory($parent)
        $clock = [Diagnostics.Stopwatch]::StartNew()
        while (-not $stream) {
            try { $stream = [IO.File]::Open($path, [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::Read) }
            catch [IO.IOException] { if ($clock.Elapsed.TotalSeconds -ge 2) { throw }; Start-Sleep -Milliseconds 25 }
        }
        $null = $stream.Seek(0, [IO.SeekOrigin]::End)
        $bytes = [Text.UTF8Encoding]::new($false).GetBytes($entry + [Environment]::NewLine)
        $stream.Write($bytes, 0, $bytes.Length)
        $stream.Flush($true)
        $report.handoff.written = $true
    } catch {
        $report.handoff.error = $_.Exception.Message
    } finally {
        if ($stream) { $stream.Dispose() }
        Save-TestAllReport $Context
    }
}
