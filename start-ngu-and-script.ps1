#requires -Version 7.0
[CmdletBinding()]
param([switch]$Legacy, [switch]$ExternalUi, [switch]$StartUi)
dynamicparam {
    $target = if ($Legacy) { Join-Path $PSScriptRoot 'start-ngu-and-script.legacy.ps1' }
        else { Join-Path $PSScriptRoot '../NGUInjector-rewrite/start-ngu-and-script.ps1' }
    $parameters = [Management.Automation.RuntimeDefinedParameterDictionary]::new()
    foreach ($parameter in (Get-Command $target).Parameters.Values) {
        if ($parameter.Name -in 'ExternalUi', 'StartUi') { continue }
        if ($parameter.Name -in [Management.Automation.PSCmdlet]::CommonParameters -or $parameter.Name -in [Management.Automation.PSCmdlet]::OptionalCommonParameters) { continue }
        $parameters.Add($parameter.Name, [Management.Automation.RuntimeDefinedParameter]::new($parameter.Name, $parameter.ParameterType, $parameter.Attributes))
    }
    $parameters
}
end {
    $forward = @{} + $PSBoundParameters; $forward.Remove('Legacy')
    # Existing shortcuts request these switches; rewrite always uses its external UI.
    if (!$Legacy) { $forward.Remove('ExternalUi'); $forward.Remove('StartUi') }
    & $target @forward
}
