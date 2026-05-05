Set-StrictMode -Version 2.0

function ConvertFrom-UniGetUIYamlText {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string] $Yaml,

        [Parameter(Mandatory = $true)]
        [string] $Path
    )

    $convertFromYamlCommand = Get-Command -Name ConvertFrom-Yaml -ErrorAction SilentlyContinue
    if ($null -ne $convertFromYamlCommand) {
        try {
            return ConvertFrom-Yaml -Yaml $Yaml -Ordered -ErrorAction Stop
        }
        catch {
            throw "Failed to parse YAML file '$Path': $($_.Exception.Message)"
        }
    }

    $pwshCommand = Get-Command -Name pwsh -ErrorAction SilentlyContinue
    if ($null -ne $pwshCommand) {
        $script = @'
$yaml = [Console]::In.ReadToEnd()
try {
    Import-Module powershell-yaml -ErrorAction Stop
    $data = ConvertFrom-Yaml -Yaml $yaml -Ordered -ErrorAction Stop
    $data | ConvertTo-Json -Depth 100
}
catch {
    Write-Error $_.Exception.Message
    exit 1
}
'@

        $output = $Yaml | & $pwshCommand.Source -NoProfile -ExecutionPolicy Bypass -Command $script 2>&1
        if ($LASTEXITCODE -ne 0) {
            throw "Failed to parse YAML file '$Path': $($output -join [Environment]::NewLine)"
        }

        try {
            return ($output -join [Environment]::NewLine) | ConvertFrom-Json -ErrorAction Stop
        }
        catch {
            throw "Failed to convert YAML file '$Path' to canonical JSON: $($_.Exception.Message)"
        }
    }

    throw "Failed to parse YAML file '$Path': ConvertFrom-Yaml is unavailable. Install the powershell-yaml module or run from a shell that can import it."
}

function Read-UniGetUIDocumentFile {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string] $Path
    )

    $resolvedPath = Resolve-Path -LiteralPath $Path -ErrorAction Stop
    $text = Get-Content -LiteralPath $resolvedPath.Path -Raw -Encoding UTF8
    $extension = [System.IO.Path]::GetExtension($resolvedPath.Path).ToLowerInvariant()
    $format = $null
    $data = $null
    $json = $null

    if ($extension -eq ".json") {
        $format = "json"
        $json = $text
        try {
            $data = $json | ConvertFrom-Json -ErrorAction Stop
        }
        catch {
            throw "Failed to parse JSON file '$Path': $($_.Exception.Message)"
        }
    }
    elseif ($extension -eq ".yaml" -or $extension -eq ".yml") {
        $format = "yaml"
        $yamlData = ConvertFrom-UniGetUIYamlText -Yaml $text -Path $Path
        try {
            $json = $yamlData | ConvertTo-Json -Depth 100
            $data = $json | ConvertFrom-Json -ErrorAction Stop
        }
        catch {
            throw "Failed to normalize YAML file '$Path' to canonical JSON: $($_.Exception.Message)"
        }
    }
    else {
        throw "Unsupported policy document extension '$extension' for '$Path'. Use .json, .yaml, or .yml."
    }

    [pscustomobject]@{
        Path = $resolvedPath.Path
        Format = $format
        Text = $text
        Json = $json
        Data = $data
    }
}

function Read-UniGetUIJsonFile {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string] $Path
    )

    Read-UniGetUIDocumentFile -Path $Path
}

function Test-UniGetUIJsonSchemaIfAvailable {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string] $Json,

        [Parameter(Mandatory = $true)]
        [string] $SchemaPath
    )

    if (-not (Test-Path -LiteralPath $SchemaPath)) {
        return [pscustomobject]@{
            UsedSchema = $false
            Passed = $true
            Message = "Schema file not found; skipped JSON Schema validation."
        }
    }

    $testJsonCommand = Get-Command -Name Test-Json -ErrorAction SilentlyContinue
    if ($null -eq $testJsonCommand) {
        return [pscustomobject]@{
            UsedSchema = $false
            Passed = $true
            Message = "Test-Json is unavailable; skipped JSON Schema validation."
        }
    }

    try {
        $schema = Get-Content -LiteralPath $SchemaPath -Raw -Encoding UTF8
        $passed = Test-Json -Json $Json -Schema $schema -ErrorAction Stop
        return [pscustomobject]@{
            UsedSchema = $true
            Passed = [bool] $passed
            Message = "JSON Schema validation completed."
        }
    }
    catch {
        return [pscustomobject]@{
            UsedSchema = $true
            Passed = $false
            Message = $_.Exception.Message
        }
    }
}

function Assert-UniGetUIPolicyShape {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [pscustomobject] $Policy
    )

    if ($Policy.policyType -ne "packageBrokerPolicy") {
        throw "Policy field 'policyType' must be 'packageBrokerPolicy'."
    }
    if ([string]::IsNullOrWhiteSpace($Policy.policyVersion)) {
        throw "Policy field 'policyVersion' is required."
    }
    if ($null -eq $Policy.metadata -or [string]::IsNullOrWhiteSpace($Policy.metadata.id)) {
        throw "Policy field 'metadata.id' is required."
    }
    if ($null -eq $Policy.enforcement) {
        throw "Policy field 'enforcement' is required."
    }
    if ($Policy.enforcement.failureDecision -ne "deny") {
        throw "Policy field 'enforcement.failureDecision' must be 'deny'."
    }
    if ($Policy.enforcement.defaultDecision -notin @("allow", "deny")) {
        throw "Policy field 'enforcement.defaultDecision' must be 'allow' or 'deny'."
    }
    if ($Policy.enforcement.rulePrecedence -ne "priorityThenDeny") {
        throw "Policy field 'enforcement.rulePrecedence' must be 'priorityThenDeny'."
    }
    if ($null -eq $Policy.rules -or @($Policy.rules).Count -eq 0) {
        throw "Policy field 'rules' must contain at least one rule."
    }

    foreach ($rule in @($Policy.rules)) {
        if ([string]::IsNullOrWhiteSpace($rule.id)) {
            throw "Each policy rule requires an 'id'."
        }
        if ($null -eq $rule.priority) {
            throw "Policy rule '$($rule.id)' requires 'priority'."
        }
        if ($rule.decision -notin @("allow", "deny")) {
            throw "Policy rule '$($rule.id)' requires decision 'allow' or 'deny'."
        }
        if ($null -eq $rule.match) {
            throw "Policy rule '$($rule.id)' requires a match object."
        }
    }
}

function Assert-UniGetUIRequestShape {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [pscustomobject] $Request
    )

    if ($Request.requestType -ne "packageOperation") {
        throw "Request field 'requestType' must be 'packageOperation'."
    }
    foreach ($requiredField in @("requestVersion", "requestId", "createdAt", "operation")) {
        if ([string]::IsNullOrWhiteSpace([string] $Request.$requiredField)) {
            throw "Request field '$requiredField' is required."
        }
    }
    if ($Request.operation -notin @("install", "update", "uninstall")) {
        throw "Request operation '$($Request.operation)' is not supported."
    }
    if ($null -eq $Request.manager -or $Request.manager.name -notin @("Winget", "PowerShell")) {
        throw "Request manager.name must be 'Winget' or 'PowerShell'."
    }
    if ($null -eq $Request.source -or [string]::IsNullOrWhiteSpace($Request.source.name)) {
        throw "Request source.name is required."
    }
    if ($null -eq $Request.package -or [string]::IsNullOrWhiteSpace($Request.package.id)) {
        throw "Request package.id is required."
    }
    if ([string]::IsNullOrWhiteSpace($Request.package.name)) {
        throw "Request package.name is required."
    }
    if ($null -eq $Request.options) {
        throw "Request options object is required."
    }
    foreach ($boolField in @("interactive", "runAsAdministrator", "skipHashCheck", "preRelease")) {
        if ($null -eq $Request.options.$boolField) {
            throw "Request options.$boolField is required."
        }
    }
    if ($null -eq $Request.broker -or $Request.broker.requestedElevation -notin @("standard", "elevated")) {
        throw "Request broker.requestedElevation must be 'standard' or 'elevated'."
    }
}

function Get-ObjectPropertyValue {
    param(
        [Parameter(Mandatory = $true)]
        [object] $InputObject,

        [Parameter(Mandatory = $true)]
        [string] $Name
    )

    if ($null -eq $InputObject) {
        return $null
    }

    $property = $InputObject.PSObject.Properties[$Name]
    if ($null -eq $property) {
        return $null
    }

    $property.Value
}

function Test-ValueInList {
    param(
        [object] $Value,
        [object] $List
    )

    if ($null -eq $List) {
        return $true
    }

    foreach ($item in @($List)) {
        if ($Value -eq $item) {
            return $true
        }
    }

    $false
}

function Test-WildcardAny {
    param(
        [string] $Value,
        [object] $Patterns
    )

    if ($null -eq $Patterns) {
        return $true
    }

    foreach ($pattern in @($Patterns)) {
        if ($Value -like [string] $pattern) {
            return $true
        }
    }

    $false
}

function Get-EffectiveRequestVersion {
    param(
        [Parameter(Mandatory = $true)]
        [pscustomobject] $Request
    )

    if (-not [string]::IsNullOrWhiteSpace([string] (Get-ObjectPropertyValue -InputObject $Request.options -Name "version"))) {
        return [string] $Request.options.version
    }
    if (-not [string]::IsNullOrWhiteSpace([string] (Get-ObjectPropertyValue -InputObject $Request.package -Name "newVersion"))) {
        return [string] $Request.package.newVersion
    }
    if (-not [string]::IsNullOrWhiteSpace([string] (Get-ObjectPropertyValue -InputObject $Request.package -Name "version"))) {
        return [string] $Request.package.version
    }

    ""
}

function Compare-PackageVersionString {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Left,

        [Parameter(Mandatory = $true)]
        [string] $Right
    )

    $leftVersion = $null
    $rightVersion = $null
    if ([System.Version]::TryParse(($Left -replace "-.+$", ""), [ref] $leftVersion) -and [System.Version]::TryParse(($Right -replace "-.+$", ""), [ref] $rightVersion)) {
        return $leftVersion.CompareTo($rightVersion)
    }

    [string]::Compare($Left, $Right, $true)
}

function Test-VersionRangeMatch {
    param(
        [string] $Version,
        [object] $VersionRange
    )

    if ($null -eq $VersionRange) {
        return $true
    }
    if ([string]::IsNullOrWhiteSpace($Version)) {
        return $false
    }
    if (($Version -match "-") -and ($VersionRange.includePrerelease -ne $true)) {
        return $false
    }
    if (-not [string]::IsNullOrWhiteSpace([string] (Get-ObjectPropertyValue -InputObject $VersionRange -Name "minVersion"))) {
        if ((Compare-PackageVersionString -Left $Version -Right $VersionRange.minVersion) -lt 0) {
            return $false
        }
    }
    if (-not [string]::IsNullOrWhiteSpace([string] (Get-ObjectPropertyValue -InputObject $VersionRange -Name "maxVersion"))) {
        if ((Compare-PackageVersionString -Left $Version -Right $VersionRange.maxVersion) -gt 0) {
            return $false
        }
    }

    $true
}

function Get-RequestDerivedFlags {
    param(
        [Parameter(Mandatory = $true)]
        [pscustomobject] $Request
    )

    $customParameterValue = Get-ObjectPropertyValue -InputObject $Request.options -Name "customParameters"
    $customParameters = @($customParameterValue)
    if ($null -eq $customParameterValue) {
        $customParameters = @()
    }

    $killBeforeOperationValue = Get-ObjectPropertyValue -InputObject $Request.options -Name "killBeforeOperation"
    $killBeforeOperation = @($killBeforeOperationValue)
    if ($null -eq $killBeforeOperationValue) {
        $killBeforeOperation = @()
    }

    $preCommand = [string] (Get-ObjectPropertyValue -InputObject $Request.options -Name "preOperationCommand")
    $postCommand = [string] (Get-ObjectPropertyValue -InputObject $Request.options -Name "postOperationCommand")
    $installLocation = [string] (Get-ObjectPropertyValue -InputObject $Request.options -Name "customInstallLocation")

    [pscustomobject]@{
        HasCustomParameters = ($customParameters.Count -gt 0)
        HasPrePostCommands = (-not [string]::IsNullOrWhiteSpace($preCommand) -or -not [string]::IsNullOrWhiteSpace($postCommand))
        HasKillBeforeOperation = ($killBeforeOperation.Count -gt 0)
        HasCustomInstallLocation = (-not [string]::IsNullOrWhiteSpace($installLocation))
        CustomParameters = $customParameters
        KillBeforeOperation = $killBeforeOperation
        CustomInstallLocation = $installLocation
    }
}

function Test-UniGetUIRuleMatch {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [pscustomobject] $Rule,

        [Parameter(Mandatory = $true)]
        [pscustomobject] $Request
    )

    if ((Get-ObjectPropertyValue -InputObject $Rule -Name "enabled") -eq $false) {
        return [pscustomobject]@{ Matched = $false; Reason = "Rule is disabled." }
    }

    $ruleMatch = $Rule.match
    $flags = Get-RequestDerivedFlags -Request $Request
    $effectiveVersion = Get-EffectiveRequestVersion -Request $Request

    $checks = @(
        @{ Name = "operations"; Matched = (Test-ValueInList -Value $Request.operation -List (Get-ObjectPropertyValue -InputObject $ruleMatch -Name "operations")) },
        @{ Name = "managers"; Matched = (Test-ValueInList -Value $Request.manager.name -List (Get-ObjectPropertyValue -InputObject $ruleMatch -Name "managers")) },
        @{ Name = "sources"; Matched = (Test-WildcardAny -Value $Request.source.name -Patterns (Get-ObjectPropertyValue -InputObject $ruleMatch -Name "sources")) },
        @{ Name = "packageIdentifiers"; Matched = (Test-WildcardAny -Value $Request.package.id -Patterns (Get-ObjectPropertyValue -InputObject $ruleMatch -Name "packageIdentifiers")) },
        @{ Name = "packageNames"; Matched = (Test-WildcardAny -Value $Request.package.name -Patterns (Get-ObjectPropertyValue -InputObject $ruleMatch -Name "packageNames")) },
        @{ Name = "versions"; Matched = (Test-ValueInList -Value $effectiveVersion -List (Get-ObjectPropertyValue -InputObject $ruleMatch -Name "versions")) },
        @{ Name = "versionRange"; Matched = (Test-VersionRangeMatch -Version $effectiveVersion -VersionRange (Get-ObjectPropertyValue -InputObject $ruleMatch -Name "versionRange")) },
        @{ Name = "scopes"; Matched = (Test-ValueInList -Value (Get-ObjectPropertyValue -InputObject $Request.options -Name "scope") -List (Get-ObjectPropertyValue -InputObject $ruleMatch -Name "scopes")) },
        @{ Name = "architectures"; Matched = (Test-ValueInList -Value (Get-ObjectPropertyValue -InputObject $Request.options -Name "architecture") -List (Get-ObjectPropertyValue -InputObject $ruleMatch -Name "architectures")) },
        @{ Name = "elevation"; Matched = (Test-ValueInList -Value $Request.broker.requestedElevation -List (Get-ObjectPropertyValue -InputObject $ruleMatch -Name "elevation")) },
        @{ Name = "runAsAdministrator"; Matched = (Test-ValueInList -Value ([bool] $Request.options.runAsAdministrator) -List (Get-ObjectPropertyValue -InputObject $ruleMatch -Name "runAsAdministrator")) },
        @{ Name = "interactive"; Matched = (Test-ValueInList -Value ([bool] $Request.options.interactive) -List (Get-ObjectPropertyValue -InputObject $ruleMatch -Name "interactive")) },
        @{ Name = "skipHashCheck"; Matched = (Test-ValueInList -Value ([bool] $Request.options.skipHashCheck) -List (Get-ObjectPropertyValue -InputObject $ruleMatch -Name "skipHashCheck")) },
        @{ Name = "preRelease"; Matched = (Test-ValueInList -Value ([bool] $Request.options.preRelease) -List (Get-ObjectPropertyValue -InputObject $ruleMatch -Name "preRelease")) },
        @{ Name = "hasCustomParameters"; Matched = (Test-ValueInList -Value ([bool] $flags.HasCustomParameters) -List (Get-ObjectPropertyValue -InputObject $ruleMatch -Name "hasCustomParameters")) },
        @{ Name = "hasCustomInstallLocation"; Matched = (Test-ValueInList -Value ([bool] $flags.HasCustomInstallLocation) -List (Get-ObjectPropertyValue -InputObject $ruleMatch -Name "hasCustomInstallLocation")) },
        @{ Name = "hasPrePostCommands"; Matched = (Test-ValueInList -Value ([bool] $flags.HasPrePostCommands) -List (Get-ObjectPropertyValue -InputObject $ruleMatch -Name "hasPrePostCommands")) },
        @{ Name = "hasKillBeforeOperation"; Matched = (Test-ValueInList -Value ([bool] $flags.HasKillBeforeOperation) -List (Get-ObjectPropertyValue -InputObject $ruleMatch -Name "hasKillBeforeOperation")) }
    )

    foreach ($check in $checks) {
        if (-not $check.Matched) {
            return [pscustomobject]@{ Matched = $false; Reason = "Selector '$($check.Name)' did not match." }
        }
    }

    $constraintResult = Test-UniGetUIRuleConstraints -Rule $Rule -Request $Request -Flags $flags
    if (-not $constraintResult.Passed) {
        return [pscustomobject]@{ Matched = $false; Reason = $constraintResult.Reason }
    }

    [pscustomobject]@{ Matched = $true; Reason = "Rule matched." }
}

function Test-UniGetUIRuleConstraints {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [pscustomobject] $Rule,

        [Parameter(Mandatory = $true)]
        [pscustomobject] $Request,

        [Parameter(Mandatory = $true)]
        [pscustomobject] $Flags
    )

    $constraints = Get-ObjectPropertyValue -InputObject $Rule -Name "constraints"
    if ($null -eq $constraints) {
        return [pscustomobject]@{ Passed = $true; Reason = "No constraints." }
    }

    $booleanConstraints = @(
        @{ Name = "allowInteractive"; IsRisky = [bool] $Request.options.interactive; Description = "interactive installation" },
        @{ Name = "allowRunAsAdministrator"; IsRisky = [bool] $Request.options.runAsAdministrator; Description = "administrator execution" },
        @{ Name = "allowSkipHashCheck"; IsRisky = [bool] $Request.options.skipHashCheck; Description = "integrity or publisher bypass" },
        @{ Name = "allowPreRelease"; IsRisky = [bool] $Request.options.preRelease; Description = "prerelease package" },
        @{ Name = "allowCustomInstallLocation"; IsRisky = [bool] $Flags.HasCustomInstallLocation; Description = "custom install location" },
        @{ Name = "allowCustomParameters"; IsRisky = [bool] $Flags.HasCustomParameters; Description = "custom package manager parameters" },
        @{ Name = "allowPrePostCommands"; IsRisky = [bool] $Flags.HasPrePostCommands; Description = "pre or post operation command" },
        @{ Name = "allowKillBeforeOperation"; IsRisky = [bool] $Flags.HasKillBeforeOperation; Description = "kill-before-operation process list" }
    )

    foreach ($constraint in $booleanConstraints) {
        $value = Get-ObjectPropertyValue -InputObject $constraints -Name $constraint.Name
        if ($value -eq $false -and $constraint.IsRisky) {
            return [pscustomobject]@{ Passed = $false; Reason = "Constraint '$($constraint.Name)' denied $($constraint.Description)." }
        }
    }

    $locationPatterns = Get-ObjectPropertyValue -InputObject $constraints -Name "allowedInstallLocationPatterns"
    if ($Flags.HasCustomInstallLocation -and $null -ne $locationPatterns -and -not (Test-WildcardAny -Value $Flags.CustomInstallLocation -Patterns $locationPatterns)) {
        return [pscustomobject]@{ Passed = $false; Reason = "Custom install location did not match an allowed pattern." }
    }

    $allowedParameters = Get-ObjectPropertyValue -InputObject $constraints -Name "allowedCustomParameters"
    $allowedParameterPatterns = Get-ObjectPropertyValue -InputObject $constraints -Name "allowedCustomParameterPatterns"
    $deniedParameters = Get-ObjectPropertyValue -InputObject $constraints -Name "deniedCustomParameters"

    foreach ($parameter in @($Flags.CustomParameters)) {
        if ($null -ne $deniedParameters -and (Test-WildcardAny -Value $parameter -Patterns $deniedParameters)) {
            return [pscustomobject]@{ Passed = $false; Reason = "Custom parameter '$parameter' matched a denied parameter pattern." }
        }

        if ($null -ne $allowedParameters -or $null -ne $allowedParameterPatterns) {
            $exactAllowed = Test-ValueInList -Value $parameter -List $allowedParameters
            $patternAllowed = Test-WildcardAny -Value $parameter -Patterns $allowedParameterPatterns
            if (-not ($exactAllowed -or $patternAllowed)) {
                return [pscustomobject]@{ Passed = $false; Reason = "Custom parameter '$parameter' was not explicitly allowed." }
            }
        }
    }

    [pscustomobject]@{ Passed = $true; Reason = "Constraints passed." }
}

function Invoke-UniGetUIPolicyDecision {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [pscustomobject] $Policy,

        [Parameter(Mandatory = $true)]
        [pscustomobject] $Request
    )

    Assert-UniGetUIPolicyShape -Policy $Policy
    Assert-UniGetUIRequestShape -Request $Request

    $matchingRules = @()
    foreach ($rule in @($Policy.rules)) {
        $matchResult = Test-UniGetUIRuleMatch -Rule $rule -Request $Request
        if ($matchResult.Matched) {
            $matchingRules += [pscustomobject]@{
                Id = $rule.id
                Priority = [int] $rule.priority
                Decision = $rule.decision
                Reason = $rule.reason
            }
        }
    }

    if ($matchingRules.Count -eq 0) {
        return [pscustomobject]@{
            Decision = $Policy.enforcement.defaultDecision
            RuleId = "<default>"
            Priority = $null
            Reason = "No enabled rule matched; using defaultDecision '$($Policy.enforcement.defaultDecision)'."
            MatchedRules = @()
        }
    }

    $ordered = @($matchingRules | Sort-Object -Property @{ Expression = "Priority"; Ascending = $true }, @{ Expression = { if ($_.Decision -eq "deny") { 0 } else { 1 } }; Ascending = $true })
    $winner = $ordered[0]

    [pscustomobject]@{
        Decision = $winner.Decision
        RuleId = $winner.Id
        Priority = $winner.Priority
        Reason = $winner.Reason
        MatchedRules = $ordered
    }
}

function Invoke-UniGetUIPolicyFileDecision {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string] $PolicyPath,

        [Parameter(Mandatory = $true)]
        [string] $RequestPath,

        [string] $PolicySchemaPath,

        [string] $RequestSchemaPath
    )

    $policyFile = Read-UniGetUIDocumentFile -Path $PolicyPath
    $requestFile = Read-UniGetUIDocumentFile -Path $RequestPath

    $policySchemaResult = $null
    $requestSchemaResult = $null
    if (-not [string]::IsNullOrWhiteSpace($PolicySchemaPath)) {
        $policySchemaResult = Test-UniGetUIJsonSchemaIfAvailable -Json $policyFile.Json -SchemaPath $PolicySchemaPath
    }
    if (-not [string]::IsNullOrWhiteSpace($RequestSchemaPath)) {
        $requestSchemaResult = Test-UniGetUIJsonSchemaIfAvailable -Json $requestFile.Json -SchemaPath $RequestSchemaPath
    }

    if ($null -ne $policySchemaResult -and -not $policySchemaResult.Passed) {
        return [pscustomobject]@{
            PolicyPath = $policyFile.Path
            RequestPath = $requestFile.Path
            RequestId = $requestFile.Data.requestId
            Decision = "deny"
            RuleId = "<validation-failure>"
            Priority = $null
            Reason = "Policy schema validation failed: $($policySchemaResult.Message)"
            ExpectedDecision = (Get-ObjectPropertyValue -InputObject (Get-ObjectPropertyValue -InputObject $requestFile.Data -Name "simulation") -Name "expectedDecision")
            PassedExpectation = $false
            SchemaValidation = [pscustomobject]@{ Policy = $policySchemaResult; Request = $requestSchemaResult }
        }
    }
    if ($null -ne $requestSchemaResult -and -not $requestSchemaResult.Passed) {
        return [pscustomobject]@{
            PolicyPath = $policyFile.Path
            RequestPath = $requestFile.Path
            RequestId = $requestFile.Data.requestId
            Decision = "deny"
            RuleId = "<validation-failure>"
            Priority = $null
            Reason = "Request schema validation failed: $($requestSchemaResult.Message)"
            ExpectedDecision = (Get-ObjectPropertyValue -InputObject (Get-ObjectPropertyValue -InputObject $requestFile.Data -Name "simulation") -Name "expectedDecision")
            PassedExpectation = $false
            SchemaValidation = [pscustomobject]@{ Policy = $policySchemaResult; Request = $requestSchemaResult }
        }
    }

    try {
        $decision = Invoke-UniGetUIPolicyDecision -Policy $policyFile.Data -Request $requestFile.Data
        $expected = Get-ObjectPropertyValue -InputObject (Get-ObjectPropertyValue -InputObject $requestFile.Data -Name "simulation") -Name "expectedDecision"
        $passedExpectation = $null
        if (-not [string]::IsNullOrWhiteSpace([string] $expected)) {
            $passedExpectation = ($decision.Decision -eq $expected)
        }

        [pscustomobject]@{
            PolicyPath = $policyFile.Path
            RequestPath = $requestFile.Path
            RequestId = $requestFile.Data.requestId
            Manager = $requestFile.Data.manager.name
            Source = $requestFile.Data.source.name
            PackageId = $requestFile.Data.package.id
            Operation = $requestFile.Data.operation
            Decision = $decision.Decision
            RuleId = $decision.RuleId
            Priority = $decision.Priority
            Reason = $decision.Reason
            ExpectedDecision = $expected
            PassedExpectation = $passedExpectation
            MatchedRules = $decision.MatchedRules
            SchemaValidation = [pscustomobject]@{ Policy = $policySchemaResult; Request = $requestSchemaResult }
        }
    }
    catch {
        [pscustomobject]@{
            PolicyPath = $policyFile.Path
            RequestPath = $requestFile.Path
            RequestId = $requestFile.Data.requestId
            Decision = "deny"
            RuleId = "<validation-failure>"
            Priority = $null
            Reason = "Semantic validation failed: $($_.Exception.Message)"
            ExpectedDecision = (Get-ObjectPropertyValue -InputObject (Get-ObjectPropertyValue -InputObject $requestFile.Data -Name "simulation") -Name "expectedDecision")
            PassedExpectation = $false
            SchemaValidation = [pscustomobject]@{ Policy = $policySchemaResult; Request = $requestSchemaResult }
        }
    }
}

Export-ModuleMember -Function @(
    "Read-UniGetUIDocumentFile",
    "Read-UniGetUIJsonFile",
    "Test-UniGetUIJsonSchemaIfAvailable",
    "Assert-UniGetUIPolicyShape",
    "Assert-UniGetUIRequestShape",
    "Invoke-UniGetUIPolicyDecision",
    "Invoke-UniGetUIPolicyFileDecision"
)