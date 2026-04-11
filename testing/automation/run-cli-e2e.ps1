$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$srcRoot = Join-Path $repoRoot 'src'
$configuration = if ($env:CONFIGURATION) { $env:CONFIGURATION } else { 'Release' }

function Resolve-BuildArtifact {
    param(
        [Parameter(Mandatory = $true)]
        [string] $ProjectName,
        [Parameter(Mandatory = $true)]
        [string] $AssemblyName
    )

    $binRoot = Join-Path (Join-Path $srcRoot $ProjectName) 'bin'
    $separator = [System.IO.Path]::DirectorySeparatorChar
    $configurationSegment = [string]::Concat($separator, $configuration, $separator)
    $targetFrameworkSegment = [string]::Concat($separator, 'net10.0', $separator)
    $processArchitecture = [System.Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString().ToLowerInvariant()
    $processArchitectureSegment = [string]::Concat($separator, $processArchitecture, $separator)
    $knownArchitectureSegments = @('x86', 'x64', 'arm64') | ForEach-Object {
        [string]::Concat($separator, $_, $separator)
    }

    $candidate = Get-ChildItem -Path $binRoot -Recurse -Filter $AssemblyName -File -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName.Contains($configurationSegment) -and $_.FullName.Contains($targetFrameworkSegment) } |
        Sort-Object `
            @{ Expression = {
                    if ($_.FullName.Contains($processArchitectureSegment)) {
                        2
                    }
                    else {
                        $hasDifferentArchitectureSegment = $false
                        foreach ($architectureSegment in $knownArchitectureSegments) {
                            if ($architectureSegment -ne $processArchitectureSegment -and $_.FullName.Contains($architectureSegment)) {
                                $hasDifferentArchitectureSegment = $true
                                break
                            }
                        }

                        if (-not $hasDifferentArchitectureSegment) {
                            1
                        }
                        else {
                            0
                        }
                    }
                }; Descending = $true },
            @{ Expression = 'LastWriteTimeUtc'; Descending = $true } |
        Select-Object -First 1

    if ($null -eq $candidate) {
        throw "Assembly $AssemblyName not found under $binRoot"
    }

    return $candidate.FullName
}

$daemonDll = Resolve-BuildArtifact -ProjectName 'UniGetUI.Avalonia' -AssemblyName 'UniGetUI.Avalonia.dll'
$cliDll = Resolve-BuildArtifact -ProjectName 'UniGetUI.Cli' -AssemblyName 'UniGetUI.Cli.dll'

if (-not (Test-Path $daemonDll)) {
    throw "Daemon assembly not found at $daemonDll"
}

if (-not (Test-Path $cliDll)) {
    throw "CLI assembly not found at $cliDll"
}

$daemonDll = (Resolve-Path $daemonDll).Path
$cliDll = (Resolve-Path $cliDll).Path

$daemonRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("unigetui-headless-" + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $daemonRoot | Out-Null

$env:HOME = $daemonRoot
$env:USERPROFILE = $daemonRoot
$env:DOTNET_CLI_HOME = $daemonRoot

$transportArgs = @()
$daemonArgs = @($daemonDll, '--headless')

if ($IsWindows) {
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

        $output = & dotnet $cliDll @Arguments @transportArgs 2>&1
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

    $search = Invoke-CliJson -Arguments @('search-packages', '--manager', '.NET Tool', '--query', 'dotnetsay', '--max-results', '20')
    $searchMatch = @($search.packages | Where-Object { $_.id -eq 'dotnetsay' })
    if ($searchMatch.Count -eq 0) {
        throw "search-packages did not return dotnetsay"
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

    $updates = Wait-ForCliCondition `
        -Arguments @('get-updates', '--manager', '.NET Tool') `
        -FailureMessage 'get-updates did not report dotnetsay after installing 2.1.4' `
        -Condition {
            param($response)
            @($response.updates | Where-Object { $_.id -eq 'dotnetsay' }).Count -gt 0
        }
    $updatableDotnetsay = @($updates.updates | Where-Object { $_.id -eq 'dotnetsay' })

    $update = Invoke-CliJson -Arguments @('update-package', '--manager', '.NET Tool', '--package-id', 'dotnetsay')
    if ($update.status -ne 'success') {
        throw "update-package failed: $($update | ConvertTo-Json -Depth 8)"
    }

    $installedAfterUpdate = Wait-ForCliCondition `
        -Arguments @('list-installed', '--manager', '.NET Tool') `
        -FailureMessage 'list-installed did not include an updated dotnetsay version after update' `
        -Condition {
            param($response)
            @($response.packages | Where-Object { $_.id -eq 'dotnetsay' -and $_.version -ne '2.1.4' }).Count -gt 0
        }
    $updatedDotnetsay = @($installedAfterUpdate.packages | Where-Object { $_.id -eq 'dotnetsay' })

    $updatesAfterUpdate = Wait-ForCliCondition `
        -Arguments @('get-updates', '--manager', '.NET Tool') `
        -FailureMessage 'dotnetsay still appears in get-updates after update' `
        -Condition {
            param($response)
            @($response.updates | Where-Object { $_.id -eq 'dotnetsay' }).Count -eq 0
        }
    $remainingDotnetsayUpdate = @($updatesAfterUpdate.updates | Where-Object { $_.id -eq 'dotnetsay' })

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
