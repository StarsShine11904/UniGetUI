[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $PolicyPath,

    [Parameter(Mandatory = $true)]
    [string[]] $RequestPath,

    [string] $PolicySchemaPath,

    [string] $RequestSchemaPath,

    [switch] $AsJson
)

Set-StrictMode -Version 2.0

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$policyRoot = Resolve-Path -LiteralPath (Join-Path $scriptRoot "..")

if ([string]::IsNullOrWhiteSpace($PolicySchemaPath)) {
    $PolicySchemaPath = Join-Path $policyRoot.Path "schemas\unigetui.package-policy.schema.1.0.json"
}
if ([string]::IsNullOrWhiteSpace($RequestSchemaPath)) {
    $RequestSchemaPath = Join-Path $policyRoot.Path "schemas\unigetui.package-request.schema.1.0.json"
}

Import-Module (Join-Path $scriptRoot "UniGetUIPolicySimulation.psm1") -Force

$expandedRequestPaths = @()
foreach ($pathArgument in $RequestPath) {
    $splitPaths = ([string] $pathArgument) -split ","
    foreach ($path in $splitPaths) {
        $trimmedPath = $path.Trim()
        if ([string]::IsNullOrWhiteSpace($trimmedPath)) {
            continue
        }

        $matches = Resolve-Path -Path $trimmedPath -ErrorAction Stop
        foreach ($match in @($matches)) {
            $expandedRequestPaths += $match.Path
        }
    }
}

$results = @()
foreach ($path in $expandedRequestPaths) {
    $results += Invoke-UniGetUIPolicyFileDecision -PolicyPath $PolicyPath -RequestPath $path -PolicySchemaPath $PolicySchemaPath -RequestSchemaPath $RequestSchemaPath
}

if ($AsJson) {
    $results | ConvertTo-Json -Depth 10
    return
}

$results |
    Select-Object RequestId, Manager, Source, PackageId, Operation, Decision, RuleId, Reason |
    Format-Table -AutoSize