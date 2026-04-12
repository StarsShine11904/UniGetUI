$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$runningOnWindows = [System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform(
    [System.Runtime.InteropServices.OSPlatform]::Windows
)

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$srcRoot = Join-Path $repoRoot 'src'
$configuration = if ($env:CONFIGURATION) { $env:CONFIGURATION } else { 'Release' }

$daemonProject = Join-Path (Join-Path $srcRoot 'UniGetUI.Avalonia') 'UniGetUI.Avalonia.csproj'
$cliProject = Join-Path (Join-Path $srcRoot 'UniGetUI.Cli') 'UniGetUI.Cli.csproj'

if (-not (Test-Path $daemonProject)) {
    throw "Daemon project not found at $daemonProject"
}

if (-not (Test-Path $cliProject)) {
    throw "CLI project not found at $cliProject"
}

$daemonProject = (Resolve-Path $daemonProject).Path
$cliProject = (Resolve-Path $cliProject).Path

$daemonRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("unigetui-headless-" + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $daemonRoot | Out-Null

$env:HOME = $daemonRoot
$env:USERPROFILE = $daemonRoot
$env:DOTNET_CLI_HOME = $daemonRoot

$transportArgs = @()
$daemonArgs = @('run', '--project', $daemonProject, '--configuration', $configuration, '--no-build', '--', '--headless')

if ($runningOnWindows) {
    $pipeName = "UniGetUI.CI.$([Guid]::NewGuid().ToString('N'))"
    $transportArgs += @('--transport', 'named-pipe', '--pipe-name', $pipeName)
    $daemonArgs += @('--background-api-transport', 'named-pipe', '--background-api-pipe-name', $pipeName)
}
else {
    $port = Get-Random -Minimum 19058 -Maximum 19999
    $transportArgs += @('--transport', 'tcp', '--tcp-port', $port)
    $daemonArgs += @('--background-api-transport', 'tcp', '--background-api-port', $port)
}

$daemonStdOutLog = Join-Path $daemonRoot 'headless-daemon.stdout.log'
$daemonStdErrLog = Join-Path $daemonRoot 'headless-daemon.stderr.log'
$process = Start-Process `
    -FilePath 'dotnet' `
    -ArgumentList $daemonArgs `
    -RedirectStandardOutput $daemonStdOutLog `
    -RedirectStandardError $daemonStdErrLog `
    -PassThru

function Stop-Daemon {
    if ($null -ne $process -and -not $process.HasExited) {
        Stop-Process -Id $process.Id
    }
}

function Get-DaemonLog {
    $stdout = if (Test-Path $daemonStdOutLog) { Get-Content $daemonStdOutLog -Raw } else { '' }
    $stderr = if (Test-Path $daemonStdErrLog) { Get-Content $daemonStdErrLog -Raw } else { '' }
    return ($stdout, $stderr -join [Environment]::NewLine).Trim()
}

try {
    function Invoke-CliJson {
        param(
            [Parameter(Mandatory = $true)]
            [string[]] $Arguments
        )

        $output = & dotnet run --project $cliProject --configuration $configuration --no-build -- @Arguments @transportArgs 2>&1
        if ($LASTEXITCODE -ne 0) {
            throw "CLI command failed ($LASTEXITCODE): $($Arguments -join ' ')`n$output"
        }

        $text = ($output -join [Environment]::NewLine).Trim()
        if ([string]::IsNullOrWhiteSpace($text)) {
            throw "CLI command returned empty output: $($Arguments -join ' ')"
        }

        return $text | ConvertFrom-Json
    }

    function Wait-ForCliCondition {
        param(
            [Parameter(Mandatory = $true)]
            [string[]] $Arguments,
            [Parameter(Mandatory = $true)]
            [scriptblock] $Condition,
            [Parameter(Mandatory = $true)]
            [string] $FailureMessage,
            [int] $TimeoutSeconds = 90,
            [int] $DelaySeconds = 3
        )

        $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
        $lastResponse = $null

        do {
            $lastResponse = Invoke-CliJson -Arguments $Arguments
            if (& $Condition $lastResponse) {
                return $lastResponse
            }

            Start-Sleep -Seconds $DelaySeconds
        } while ((Get-Date) -lt $deadline)

        throw "$FailureMessage`nLast payload: $($lastResponse | ConvertTo-Json -Depth 8)"
    }

    $deadline = (Get-Date).AddMinutes(2)
    do {
        Start-Sleep -Seconds 2

        if ($process.HasExited) {
            $daemonOutput = Get-DaemonLog
            throw "Headless daemon exited early with code $($process.ExitCode).`n$daemonOutput"
        }

        $status = Invoke-CliJson -Arguments @('status')
    } while (-not $status.running -and (Get-Date) -lt $deadline)

    if (-not $status.running) {
        $daemonOutput = Get-DaemonLog
        throw "Headless daemon never became ready.`n$daemonOutput"
    }

    $managers = Invoke-CliJson -Arguments @('list-managers')
    if (@($managers.managers | Where-Object { $_.name -eq '.NET Tool' }).Count -eq 0) {
        throw "list-managers did not report the .NET Tool manager"
    }

    $sources = Invoke-CliJson -Arguments @('list-sources', '--manager', '.NET Tool')
    if (@($sources.sources | Where-Object { $_.name -eq 'nuget.org' }).Count -eq 0) {
        throw "list-sources did not report nuget.org for .NET Tool"
    }

    $settings = Invoke-CliJson -Arguments @('list-settings')
    if (@($settings.settings | Where-Object { $_.name -eq 'FreshValue' }).Count -eq 0) {
        throw "list-settings did not report FreshValue"
    }

    $setFreshValue = Invoke-CliJson -Arguments @('set-setting', '--key', 'FreshValue', '--value', 'cli-smoke')
    if ($setFreshValue.setting.stringValue -ne 'cli-smoke') {
        throw "set-setting did not persist FreshValue"
    }

    $getFreshValue = Invoke-CliJson -Arguments @('get-setting', '--key', 'FreshValue')
    if ($getFreshValue.setting.stringValue -ne 'cli-smoke') {
        throw "get-setting did not return the FreshValue payload"
    }

    $setFreshBool = Invoke-CliJson -Arguments @('set-setting', '--key', 'FreshBoolSetting', '--enabled', 'true')
    if (-not $setFreshBool.setting.boolValue) {
        throw "set-setting did not enable FreshBoolSetting"
    }

    $search = Invoke-CliJson -Arguments @('search-packages', '--manager', '.NET Tool', '--query', 'dotnetsay', '--max-results', '20')
    $searchMatch = @($search.packages | Where-Object { $_.id -eq 'dotnetsay' })
    if ($searchMatch.Count -eq 0) {
        throw "search-packages did not return dotnetsay"
    }
    $latestDotnetsayVersion = $searchMatch[0].version
    if ([string]::IsNullOrWhiteSpace($latestDotnetsayVersion)) {
        throw "search-packages did not report the latest dotnetsay version"
    }

    $details = Invoke-CliJson -Arguments @('package-details', '--manager', '.NET Tool', '--package-id', 'dotnetsay')
    if ($details.package.id -ne 'dotnetsay') {
        throw "package-details did not return dotnetsay"
    }

    $versions = Invoke-CliJson -Arguments @('package-versions', '--manager', '.NET Tool', '--package-id', 'dotnetsay')
    if (@($versions.versions | Where-Object { $_ -eq '2.1.4' }).Count -eq 0) {
        throw "package-versions did not report version 2.1.4 for dotnetsay"
    }

    $install = Invoke-CliJson -Arguments @('install-package', '--manager', '.NET Tool', '--package-id', 'dotnetsay', '--version', '2.1.4', '--scope', 'Global')
    if ($install.status -ne 'success') {
        throw "install-package failed: $($install | ConvertTo-Json -Depth 8)"
    }

    $installed = Wait-ForCliCondition `
        -Arguments @('list-installed', '--manager', '.NET Tool') `
        -FailureMessage 'list-installed did not include dotnetsay after installation' `
        -Condition {
            param($response)
            @($response.packages | Where-Object { $_.id -eq 'dotnetsay' }).Count -gt 0
        }
    $installedDotnetsay = @($installed.packages | Where-Object { $_.id -eq 'dotnetsay' })

    $installed = Wait-ForCliCondition `
        -Arguments @('list-installed', '--manager', '.NET Tool') `
        -FailureMessage 'dotnetsay did not report version 2.1.4 after installation' `
        -Condition {
            param($response)
            @($response.packages | Where-Object { $_.id -eq 'dotnetsay' -and $_.version -eq '2.1.4' }).Count -gt 0
        }
    $installedDotnetsay = @($installed.packages | Where-Object { $_.id -eq 'dotnetsay' })

    $ignore = Invoke-CliJson -Arguments @('ignore-package', '--manager', '.NET Tool', '--package-id', 'dotnetsay')
    if ($ignore.status -ne 'success') {
        throw "ignore-package failed: $($ignore | ConvertTo-Json -Depth 8)"
    }

    $ignoredUpdates = Invoke-CliJson -Arguments @('list-ignored-updates')
    if (@($ignoredUpdates.ignoredUpdates | Where-Object { $_.packageId -eq 'dotnetsay' }).Count -eq 0) {
        throw "list-ignored-updates did not report dotnetsay after ignore-package"
    }

    $unignore = Invoke-CliJson -Arguments @('unignore-package', '--manager', '.NET Tool', '--package-id', 'dotnetsay')
    if ($unignore.status -ne 'success') {
        throw "unignore-package failed: $($unignore | ConvertTo-Json -Depth 8)"
    }

    $ignoredUpdates = Invoke-CliJson -Arguments @('list-ignored-updates')
    if (@($ignoredUpdates.ignoredUpdates | Where-Object { $_.packageId -eq 'dotnetsay' }).Count -ne 0) {
        throw "unignore-package did not remove dotnetsay from ignored updates"
    }

    $update = Invoke-CliJson -Arguments @('update-package', '--manager', '.NET Tool', '--package-id', 'dotnetsay', '--version', $latestDotnetsayVersion)
    if ($update.status -ne 'success') {
        throw "update-package failed: $($update | ConvertTo-Json -Depth 8)"
    }

    $installedAfterUpdate = Wait-ForCliCondition `
        -Arguments @('list-installed', '--manager', '.NET Tool') `
        -FailureMessage 'list-installed did not include an updated dotnetsay version after update' `
        -Condition {
            param($response)
            @($response.packages | Where-Object { $_.id -eq 'dotnetsay' -and $_.version -eq $latestDotnetsayVersion }).Count -gt 0
        }
    $updatedDotnetsay = @($installedAfterUpdate.packages | Where-Object { $_.id -eq 'dotnetsay' })

    $uninstall = Invoke-CliJson -Arguments @('uninstall-package', '--manager', '.NET Tool', '--package-id', 'dotnetsay', '--scope', 'Global')
    if ($uninstall.status -ne 'success') {
        throw "uninstall-package failed: $($uninstall | ConvertTo-Json -Depth 8)"
    }

    $installedAfterUninstall = Wait-ForCliCondition `
        -Arguments @('list-installed', '--manager', '.NET Tool') `
        -FailureMessage 'dotnetsay still appears in list-installed after uninstall' `
        -Condition {
            param($response)
            @($response.packages | Where-Object { $_.id -eq 'dotnetsay' }).Count -eq 0
        }
    $remainingDotnetsay = @($installedAfterUninstall.packages | Where-Object { $_.id -eq 'dotnetsay' })

    $clearFreshValue = Invoke-CliJson -Arguments @('clear-setting', '--key', 'FreshValue')
    if ($clearFreshValue.setting.isSet) {
        throw "clear-setting did not clear FreshValue"
    }

    $disableFreshBool = Invoke-CliJson -Arguments @('set-setting', '--key', 'FreshBoolSetting', '--enabled', 'false')
    if ($disableFreshBool.setting.boolValue) {
        throw "set-setting did not disable FreshBoolSetting"
    }
}
finally {
    Stop-Daemon

    $daemonLog = Get-DaemonLog
    if (-not [string]::IsNullOrWhiteSpace($daemonLog)) {
        Write-Host '--- Headless daemon log ---'
        Write-Host $daemonLog
    }

    Remove-Item -Recurse -Force $daemonRoot -ErrorAction SilentlyContinue
}
