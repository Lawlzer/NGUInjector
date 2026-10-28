#requires -Version 7.0
[CmdletBinding()]
param([switch]$Legacy)
dynamicparam {
    # The preserved legacy game launcher calls this wrapper internally.
    $fromLegacy = @(Get-PSCallStack | Where-Object ScriptName -eq (Join-Path $PSScriptRoot 'start-ngu-and-script.legacy.ps1')).Count -gt 0
    $target = if ($Legacy -or $fromLegacy) { Join-Path $PSScriptRoot 'start-ui.legacy.ps1' }
        else { Join-Path $PSScriptRoot '../NGUInjector-rewrite/start-ui.ps1' }
    $parameters = [Management.Automation.RuntimeDefinedParameterDictionary]::new()
    foreach ($parameter in (Get-Command $target).Parameters.Values) {
        if ($parameter.Name -in [Management.Automation.PSCmdlet]::CommonParameters -or $parameter.Name -in [Management.Automation.PSCmdlet]::OptionalCommonParameters) { continue }
        $parameters.Add($parameter.Name, [Management.Automation.RuntimeDefinedParameter]::new($parameter.Name, $parameter.ParameterType, $parameter.Attributes))
    }
    $parameters
}
end {
    $forward = @{} + $PSBoundParameters; $forward.Remove('Legacy')
    & $target @forward
}
