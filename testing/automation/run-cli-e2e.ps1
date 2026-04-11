$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$srcRoot = Join-Path $repoRoot 'src'
$configuration = if ($env:CONFIGURATION) { $env:CONFIGURATION } else { 'Release' }

$daemonDll = Join-Path $srcRoot "UniGetUI.Avalonia\bin\$configuration\net10.0\UniGetUI.Avalonia.dll"
$cliDll = Join-Path $srcRoot "UniGetUI.Cli\bin\$configuration\net10.0\UniGetUI.Cli.dll"

if (-not (Test-Path $daemonDll)) {
    throw "Daemon assembly not found at $daemonDll"
}

if (-not (Test-Path $cliDll)) {
    throw "CLI assembly not found at $cliDll"
}

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

    $installed = Invoke-CliJson -Arguments @('list-installed', '--manager', '.NET Tool')
    $installedDotnetsay = @($installed.packages | Where-Object { $_.id -eq 'dotnetsay' })
    if ($installedDotnetsay.Count -eq 0) {
        throw "list-installed did not include dotnetsay after installation"
    }

    if (-not ($installedDotnetsay.version -contains '2.1.4')) {
        throw "Expected dotnetsay version 2.1.4 after install. Found: $($installedDotnetsay.version -join ', ')"
    }

    $updates = Invoke-CliJson -Arguments @('get-updates', '--manager', '.NET Tool')
    $updatableDotnetsay = @($updates.updates | Where-Object { $_.id -eq 'dotnetsay' })
    if ($updatableDotnetsay.Count -eq 0) {
        throw "get-updates did not report dotnetsay after installing 2.1.4"
    }

    $update = Invoke-CliJson -Arguments @('update-package', '--manager', '.NET Tool', '--package-id', 'dotnetsay')
    if ($update.status -ne 'success') {
        throw "update-package failed: $($update | ConvertTo-Json -Depth 8)"
    }

    $installedAfterUpdate = Invoke-CliJson -Arguments @('list-installed', '--manager', '.NET Tool')
    $updatedDotnetsay = @($installedAfterUpdate.packages | Where-Object { $_.id -eq 'dotnetsay' })
    if ($updatedDotnetsay.Count -eq 0) {
        throw "list-installed did not include dotnetsay after update"
    }

    if ($updatedDotnetsay.version -contains '2.1.4') {
        throw "dotnetsay still reports version 2.1.4 after update"
    }

    $updatesAfterUpdate = Invoke-CliJson -Arguments @('get-updates', '--manager', '.NET Tool')
    $remainingDotnetsayUpdate = @($updatesAfterUpdate.updates | Where-Object { $_.id -eq 'dotnetsay' })
    if ($remainingDotnetsayUpdate.Count -ne 0) {
        throw "dotnetsay still appears in get-updates after update"
    }

    $uninstall = Invoke-CliJson -Arguments @('uninstall-package', '--manager', '.NET Tool', '--package-id', 'dotnetsay', '--scope', 'Global')
    if ($uninstall.status -ne 'success') {
        throw "uninstall-package failed: $($uninstall | ConvertTo-Json -Depth 8)"
    }

    $installedAfterUninstall = Invoke-CliJson -Arguments @('list-installed', '--manager', '.NET Tool')
    $remainingDotnetsay = @($installedAfterUninstall.packages | Where-Object { $_.id -eq 'dotnetsay' })
    if ($remainingDotnetsay.Count -ne 0) {
        throw "dotnetsay still appears in list-installed after uninstall"
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
