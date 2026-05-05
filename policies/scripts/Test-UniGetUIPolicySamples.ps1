[CmdletBinding()]
param(
    [string[]] $ScenarioPath,

    [string[]] $Tag,

    [switch] $List
)

Set-StrictMode -Version 2.0

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$policyRoot = Resolve-Path -LiteralPath (Join-Path $scriptRoot "..")

Import-Module (Join-Path $scriptRoot "UniGetUIPolicySimulation.psm1") -Force

$policySchemaPath = Join-Path $policyRoot.Path "schemas\unigetui.package-policy.schema.1.0.json"
$requestSchemaPath = Join-Path $policyRoot.Path "schemas\unigetui.package-request.schema.1.0.json"
$sampleRoot = Join-Path $policyRoot.Path "samples"
$scenarioRoot = Join-Path $sampleRoot "scenarios"

function Resolve-SampleRelativePath {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Path
    )

    if ([System.IO.Path]::IsPathRooted($Path)) {
        return (Resolve-Path -LiteralPath $Path -ErrorAction Stop).Path
    }

    (Resolve-Path -LiteralPath (Join-Path $sampleRoot $Path) -ErrorAction Stop).Path
}

function Get-ScenarioManifestPaths {
    param(
        [string[]] $Paths
    )

    $resolvedPaths = @()
    if ($null -eq $Paths -or $Paths.Count -eq 0) {
        foreach ($filter in @("*.scenarios.json", "*.scenarios.yaml", "*.scenarios.yml")) {
            $resolvedPaths += @(Get-ChildItem -LiteralPath $scenarioRoot -Filter $filter -File -ErrorAction SilentlyContinue | Select-Object -ExpandProperty FullName)
        }
    }
    else {
        foreach ($path in $Paths) {
            $matches = Resolve-Path -Path $path -ErrorAction Stop
            foreach ($match in @($matches)) {
                $resolvedPaths += $match.Path
            }
        }
    }

    @($resolvedPaths | Sort-Object -Unique)
}

function Test-ScenarioTagMatch {
    param(
        [object] $ScenarioTags,
        [string[]] $RequiredTags
    )

    if ($null -eq $RequiredTags -or $RequiredTags.Count -eq 0) {
        return $true
    }

    $tags = @($ScenarioTags)
    foreach ($requiredTag in $RequiredTags) {
        if ($tags -contains $requiredTag) {
            return $true
        }
    }

    $false
}

$manifestPaths = Get-ScenarioManifestPaths -Paths $ScenarioPath
if ($manifestPaths.Count -eq 0) {
    Write-Error "No scenario manifests were found."
    exit 1
}

$scenarios = @()
foreach ($manifestPath in $manifestPaths) {
    $manifest = Read-UniGetUIDocumentFile -Path $manifestPath
    $manifestScenarios = Get-ObjectPropertyValue -InputObject $manifest.Data -Name "scenarios"
    if ($null -eq $manifestScenarios) {
        Write-Error "Scenario manifest '$manifestPath' does not contain a scenarios array."
        exit 1
    }

    foreach ($scenario in @($manifestScenarios)) {
        if (-not (Test-ScenarioTagMatch -ScenarioTags (Get-ObjectPropertyValue -InputObject $scenario -Name "tags") -RequiredTags $Tag)) {
            continue
        }

        $scenarios += [pscustomobject]@{
            Manifest = $manifest.Path
            Id = [string] (Get-ObjectPropertyValue -InputObject $scenario -Name "id")
            Policy = [string] (Get-ObjectPropertyValue -InputObject $scenario -Name "policy")
            Request = [string] (Get-ObjectPropertyValue -InputObject $scenario -Name "request")
            ExpectedDecision = [string] (Get-ObjectPropertyValue -InputObject $scenario -Name "expectedDecision")
            ExpectedRuleId = [string] (Get-ObjectPropertyValue -InputObject $scenario -Name "expectedRuleId")
            Tags = @((Get-ObjectPropertyValue -InputObject $scenario -Name "tags"))
            Description = [string] (Get-ObjectPropertyValue -InputObject $scenario -Name "description")
        }
    }
}

if ($scenarios.Count -eq 0) {
    Write-Error "No scenarios matched the requested filters."
    exit 1
}

if ($List) {
    $scenarios |
        Select-Object Id, @{ Name = "Tags"; Expression = { ($_.Tags -join ",") } }, Policy, Request, ExpectedDecision, ExpectedRuleId, Description |
        Format-Table -AutoSize
    return
}

$results = @()
foreach ($scenario in $scenarios) {
    if ([string]::IsNullOrWhiteSpace($scenario.Id)) {
        Write-Error "A scenario in '$($scenario.Manifest)' is missing an id."
        exit 1
    }
    if ([string]::IsNullOrWhiteSpace($scenario.Policy)) {
        Write-Error "Scenario '$($scenario.Id)' is missing a policy path."
        exit 1
    }
    if ([string]::IsNullOrWhiteSpace($scenario.Request)) {
        Write-Error "Scenario '$($scenario.Id)' is missing a request path."
        exit 1
    }
    if ($scenario.ExpectedDecision -notin @("allow", "deny")) {
        Write-Error "Scenario '$($scenario.Id)' must declare expectedDecision 'allow' or 'deny'."
        exit 1
    }

    $policyPath = Resolve-SampleRelativePath -Path $scenario.Policy
    $requestPath = Resolve-SampleRelativePath -Path $scenario.Request
    $result = Invoke-UniGetUIPolicyFileDecision -PolicyPath $policyPath -RequestPath $requestPath -PolicySchemaPath $policySchemaPath -RequestSchemaPath $requestSchemaPath
    $decisionPassed = ($result.Decision -eq $scenario.ExpectedDecision)
    $rulePassed = $true
    if (-not [string]::IsNullOrWhiteSpace($scenario.ExpectedRuleId)) {
        $rulePassed = ($result.RuleId -eq $scenario.ExpectedRuleId)
    }

    $results += [pscustomobject]@{
        ScenarioId = $scenario.Id
        Tags = ($scenario.Tags -join ",")
        Expected = $scenario.ExpectedDecision
        Actual = $result.Decision
        RuleId = $result.RuleId
        ExpectedRuleId = $scenario.ExpectedRuleId
        Passed = ($decisionPassed -and $rulePassed)
        Reason = $result.Reason
    }
}

$results | Format-Table -AutoSize

$failures = @($results | Where-Object { -not $_.Passed })
if ($failures.Count -gt 0) {
    Write-Error "$($failures.Count) sample policy simulation scenario(s) failed."
    exit 1
}

Write-Host "All sample policy simulation scenarios passed."