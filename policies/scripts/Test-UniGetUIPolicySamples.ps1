[CmdletBinding()]
param()

Set-StrictMode -Version 2.0

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$policyRoot = Resolve-Path -LiteralPath (Join-Path $scriptRoot "..")

Import-Module (Join-Path $scriptRoot "UniGetUIPolicySimulation.psm1") -Force

$policySchemaPath = Join-Path $policyRoot.Path "schemas\unigetui.package-policy.schema.1.0.json"
$requestSchemaPath = Join-Path $policyRoot.Path "schemas\unigetui.package-request.schema.1.0.json"
$sampleRoot = Join-Path $policyRoot.Path "samples"
$requestRoot = Join-Path $sampleRoot "requests"

$cases = @(
    @{ Policy = "corporate-allowlist.policy.json"; Request = "winget-vscode-install.request.json"; Expected = "allow" },
    @{ Policy = "corporate-allowlist.policy.json"; Request = "winget-unknown-install.request.json"; Expected = "deny" },
    @{ Policy = "corporate-allowlist.policy.json"; Request = "winget-vscode-skiphash.request.json"; Expected = "deny" },
    @{ Policy = "deny-risky-options.policy.json"; Request = "winget-vscode-install.request.json"; Expected = "allow" },
    @{ Policy = "deny-risky-options.policy.json"; Request = "winget-vscode-skiphash.request.json"; Expected = "deny" },
    @{ Policy = "deny-risky-options.policy.json"; Request = "winget-vscode-custom-param.request.json"; Expected = "deny" },
    @{ Policy = "deny-risky-options.policy.json"; Request = "winget-vscode-msstore.request.json"; Expected = "deny" },
    @{ Policy = "powershell-current-user.policy.json"; Request = "powershell-pester-currentuser.request.json"; Expected = "allow" },
    @{ Policy = "powershell-current-user.policy.json"; Request = "powershell-pester-allusers.request.json"; Expected = "deny" }
)

$results = @()
foreach ($case in $cases) {
    $policyPath = Join-Path $sampleRoot $case.Policy
    $requestPath = Join-Path $requestRoot $case.Request
    $result = Invoke-UniGetUIPolicyFileDecision -PolicyPath $policyPath -RequestPath $requestPath -PolicySchemaPath $policySchemaPath -RequestSchemaPath $requestSchemaPath
    $passed = ($result.Decision -eq $case.Expected)

    $results += [pscustomobject]@{
        Policy = $case.Policy
        Request = $case.Request
        Expected = $case.Expected
        Actual = $result.Decision
        RuleId = $result.RuleId
        Passed = $passed
        Reason = $result.Reason
    }
}

$results | Format-Table -AutoSize

$failures = @($results | Where-Object { -not $_.Passed })
if ($failures.Count -gt 0) {
    Write-Error "$($failures.Count) sample policy simulation case(s) failed."
    exit 1
}

Write-Host "All sample policy simulation cases passed."