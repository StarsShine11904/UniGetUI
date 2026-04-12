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
$downloadRoot = Join-Path $daemonRoot 'downloads'
New-Item -ItemType Directory -Path $downloadRoot | Out-Null

$env:HOME = $daemonRoot
$env:USERPROFILE = $daemonRoot
$env:DOTNET_CLI_HOME = $daemonRoot
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'

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

function Write-Stage {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Name
    )

    Write-Host "== $Name =="
}

try {
    function Invoke-CliJson {
        param(
            [Parameter(Mandatory = $true)]
            [string[]] $Arguments
        )

        $commandArguments = @(
            'run',
            '--project', $cliProject,
            '--configuration', $configuration,
            '--no-build',
            '--'
        ) + $Arguments + $transportArgs
        $output = & dotnet $commandArguments 2>&1
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

    Write-Stage 'Manager and settings inspection'
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

    $secureSettings = Invoke-CliJson -Arguments @('list-secure-settings')
    if (@($secureSettings.settings | Where-Object { $_.key -eq 'AllowCLIArguments' }).Count -eq 0) {
        throw "list-secure-settings did not report AllowCLIArguments"
    }

    $managerMaintenance = Invoke-CliJson -Arguments @('get-manager-maintenance', '--manager', '.NET Tool')
    if ($managerMaintenance.maintenance.manager -ne '.NET Tool') {
        throw "get-manager-maintenance did not return the .NET Tool manager payload"
    }
    if (@($managerMaintenance.maintenance.supportedActions | Where-Object { $_ -eq 'reload' }).Count -eq 0) {
        throw "get-manager-maintenance did not expose the reload action"
    }

    $disableManagerNotifs = Invoke-CliJson -Arguments @(
        'set-manager-update-notifications',
        '--manager', '.NET Tool',
        '--enabled', 'false'
    )
    if (-not $disableManagerNotifs.manager.notificationsSuppressed) {
        throw "set-manager-update-notifications did not suppress notifications for .NET Tool"
    }

    $enableManagerNotifs = Invoke-CliJson -Arguments @(
        'set-manager-update-notifications',
        '--manager', '.NET Tool',
        '--enabled', 'true'
    )
    if ($enableManagerNotifs.manager.notificationsSuppressed) {
        throw "set-manager-update-notifications did not re-enable notifications for .NET Tool"
    }

    $reloadManager = Invoke-CliJson -Arguments @('reload-manager', '--manager', '.NET Tool')
    if ($reloadManager.operationStatus -ne 'completed') {
        throw "reload-manager did not complete successfully"
    }

    $syntheticShortcut = Join-Path $daemonRoot 'SyntheticShortcut.lnk'
    New-Item -ItemType File -Path $syntheticShortcut | Out-Null

    $keepShortcut = Invoke-CliJson -Arguments @('set-desktop-shortcut', '--path', $syntheticShortcut, '--status', 'keep')
    if ($keepShortcut.shortcut.status -ne 'keep') {
        throw "set-desktop-shortcut did not persist the keep verdict"
    }

    $shortcuts = Invoke-CliJson -Arguments @('list-desktop-shortcuts')
    if (@($shortcuts.shortcuts | Where-Object { $_.path -eq $syntheticShortcut -and $_.status -eq 'keep' -and $_.existsOnDisk }).Count -eq 0) {
        throw "list-desktop-shortcuts did not report the kept synthetic shortcut"
    }

    $deleteShortcut = Invoke-CliJson -Arguments @('set-desktop-shortcut', '--path', $syntheticShortcut, '--status', 'delete')
    if ($deleteShortcut.shortcut.status -ne 'delete') {
        throw "set-desktop-shortcut did not persist the delete verdict"
    }
    if (Test-Path $syntheticShortcut) {
        throw "set-desktop-shortcut --status delete did not delete the synthetic shortcut from disk"
    }

    $shortcutsAfterDelete = Invoke-CliJson -Arguments @('list-desktop-shortcuts')
    if (@($shortcutsAfterDelete.shortcuts | Where-Object { $_.path -eq $syntheticShortcut -and $_.status -eq 'delete' -and -not $_.existsOnDisk }).Count -eq 0) {
        throw "list-desktop-shortcuts did not report the deleted synthetic shortcut"
    }

    $resetShortcut = Invoke-CliJson -Arguments @('reset-desktop-shortcut', '--path', $syntheticShortcut)
    if ($resetShortcut.shortcut.status -ne 'unknown') {
        throw "reset-desktop-shortcut did not clear the verdict"
    }

    $shortcutsAfterReset = Invoke-CliJson -Arguments @('list-desktop-shortcuts')
    if (@($shortcutsAfterReset.shortcuts | Where-Object { $_.path -eq $syntheticShortcut }).Count -ne 0) {
        throw "reset-desktop-shortcut did not remove the synthetic shortcut from the tracked list"
    }

    $resetAllShortcuts = Invoke-CliJson -Arguments @('reset-desktop-shortcuts')
    if ($resetAllShortcuts.status -ne 'success') {
        throw "reset-desktop-shortcuts failed: $($resetAllShortcuts | ConvertTo-Json -Depth 8)"
    }

    $appLog = Invoke-CliJson -Arguments @('get-app-log', '--level', '5')
    if (@($appLog.entries).Count -eq 0) {
        throw "get-app-log returned no entries"
    }
    if (@($appLog.entries | Where-Object { -not [string]::IsNullOrWhiteSpace($_.content) }).Count -eq 0) {
        throw "get-app-log did not return readable log content"
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

    Write-Stage 'Backup inspection'
    $backupStatus = Invoke-CliJson -Arguments @('get-backup-status')
    if ([string]::IsNullOrWhiteSpace($backupStatus.backup.backupDirectory)) {
        throw "get-backup-status did not report the resolved backup directory"
    }
    if ($backupStatus.backup.auth.isAuthenticated) {
        throw "get-backup-status unexpectedly reported an authenticated GitHub backup session in the isolated test profile"
    }

    $backupDirectory = Join-Path $daemonRoot 'backups'
    $setBackupDirectory = Invoke-CliJson -Arguments @(
        'set-setting',
        '--key', 'ChangeBackupOutputDirectory',
        '--value', $backupDirectory
    )
    if ($setBackupDirectory.setting.stringValue -ne $backupDirectory) {
        throw "set-setting did not persist ChangeBackupOutputDirectory"
    }

    $setBackupFileName = Invoke-CliJson -Arguments @(
        'set-setting',
        '--key', 'ChangeBackupFileName',
        '--value', 'cli-e2e-backup'
    )
    if ($setBackupFileName.setting.stringValue -ne 'cli-e2e-backup') {
        throw "set-setting did not persist ChangeBackupFileName"
    }

    $disableBackupTimestamping = Invoke-CliJson -Arguments @(
        'set-setting',
        '--key', 'EnableBackupTimestamping',
        '--enabled', 'false'
    )
    if ($disableBackupTimestamping.setting.boolValue) {
        throw "set-setting did not disable EnableBackupTimestamping"
    }

    $localBackup = Invoke-CliJson -Arguments @('create-local-backup')
    if ($localBackup.status -ne 'success') {
        throw "create-local-backup failed: $($localBackup | ConvertTo-Json -Depth 8)"
    }
    if (-not (Test-Path $localBackup.path)) {
        throw "create-local-backup did not write the reported backup file"
    }
    if (-not $localBackup.path.EndsWith('cli-e2e-backup.ubundle')) {
        throw "create-local-backup did not honor the configured backup file name"
    }
    $localBackupContents = Get-Content $localBackup.path -Raw
    if ($localBackupContents -notmatch '"packages"' -or $localBackupContents -notmatch '"export_version"') {
        throw "create-local-backup did not write recognizable bundle content"
    }

    Write-Stage 'Package discovery'
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

    $download = Invoke-CliJson -Arguments @(
        'download-package',
        '--manager', '.NET Tool',
        '--package-id', 'dotnetsay',
        '--output', $downloadRoot
    )
    if ($download.status -ne 'success' -or [string]::IsNullOrWhiteSpace($download.outputPath)) {
        throw "download-package failed: $($download | ConvertTo-Json -Depth 8)"
    }
    if (-not (Test-Path $download.outputPath)) {
        throw "download-package did not create the downloaded file at $($download.outputPath)"
    }

    Write-Stage 'Bundle roundtrip'
    Write-Host ' - reset bundle'
    $resetBundle = Invoke-CliJson -Arguments @('reset-bundle')
    if ($resetBundle.status -ne 'success') {
        throw "reset-bundle failed: $($resetBundle | ConvertTo-Json -Depth 8)"
    }

    Write-Host ' - get empty bundle'
    $bundleAfterReset = Invoke-CliJson -Arguments @('get-bundle')
    if ($bundleAfterReset.bundle.packageCount -ne 0) {
        throw "get-bundle did not return an empty bundle after reset-bundle"
    }

    Write-Host ' - add package to bundle'
    $addBundlePackage = Invoke-CliJson -Arguments @(
        'add-bundle-package',
        '--manager', '.NET Tool',
        '--package-id', 'dotnetsay',
        '--version', '2.1.4',
        '--scope', 'Global',
        '--selection', 'search'
    )
    if ($addBundlePackage.package.id -ne 'dotnetsay') {
        throw "add-bundle-package did not add dotnetsay to the current bundle"
    }

    Write-Host ' - inspect bundle contents'
    $bundle = Invoke-CliJson -Arguments @('get-bundle')
    if (@($bundle.bundle.packages | Where-Object { $_.id -eq 'dotnetsay' -and $_.selectedVersion -eq '2.1.4' }).Count -eq 0) {
        throw "get-bundle did not return dotnetsay with the selected install version"
    }

    Write-Host ' - export bundle'
    $exportedBundle = Invoke-CliJson -Arguments @('export-bundle')
    if ([string]::IsNullOrWhiteSpace($exportedBundle.content) -or $exportedBundle.content -notmatch '"dotnetsay"') {
        throw "export-bundle did not return serialized bundle content"
    }
    $bundleRoundtripPath = Join-Path $daemonRoot 'BundleRoundtrip.json'
    Set-Content -Path $bundleRoundtripPath -Value $exportedBundle.content -Encoding UTF8

    Write-Host ' - remove package from bundle'
    $removeBundlePackage = Invoke-CliJson -Arguments @(
        'remove-bundle-package',
        '--manager', '.NET Tool',
        '--package-id', 'dotnetsay'
    )
    if ($removeBundlePackage.removedCount -lt 1) {
        throw "remove-bundle-package did not remove dotnetsay from the current bundle"
    }

    Write-Host ' - confirm bundle removal'
    $bundleAfterRemove = Invoke-CliJson -Arguments @('get-bundle')
    if ($bundleAfterRemove.bundle.packageCount -ne 0) {
        throw "remove-bundle-package did not leave the current bundle empty"
    }

    Write-Host ' - import exported bundle content'
    $importBundle = Invoke-CliJson -Arguments @(
        'import-bundle',
        '--path', $bundleRoundtripPath
    )
    if (
        $importBundle.status -ne 'success' -or
        @($importBundle.bundle.packages | Where-Object { $_.id -eq 'dotnetsay' -and $_.selectedVersion -eq '2.1.4' }).Count -eq 0
    ) {
        throw "import-bundle did not restore dotnetsay from the exported bundle content"
    }

    Write-Stage 'Package lifecycle'
    Write-Host ' - install package directly'
    $install = Invoke-CliJson -Arguments @('install-package', '--manager', '.NET Tool', '--package-id', 'dotnetsay', '--version', '2.1.4', '--scope', 'Global')
    if ($install.status -ne 'success') {
        throw "install-package failed: $($install | ConvertTo-Json -Depth 8)"
    }

    Write-Host ' - wait for installed package to appear'
    $installed = Wait-ForCliCondition `
        -Arguments @('list-installed', '--manager', '.NET Tool') `
        -FailureMessage 'list-installed did not include dotnetsay after installation' `
        -Condition {
            param($response)
            @($response.packages | Where-Object { $_.id -eq 'dotnetsay' }).Count -gt 0
        }
    $installedDotnetsay = @($installed.packages | Where-Object { $_.id -eq 'dotnetsay' })

    Write-Host ' - ignore package updates'
    $ignore = Invoke-CliJson -Arguments @('ignore-package', '--manager', '.NET Tool', '--package-id', 'dotnetsay')
    if ($ignore.status -ne 'success') {
        throw "ignore-package failed: $($ignore | ConvertTo-Json -Depth 8)"
    }

    $ignoredUpdates = Invoke-CliJson -Arguments @('list-ignored-updates')
    if (@($ignoredUpdates.ignoredUpdates | Where-Object { $_.packageId -eq 'dotnetsay' }).Count -eq 0) {
        throw "list-ignored-updates did not report dotnetsay after ignore-package"
    }

    Write-Host ' - remove ignored update'
    $unignore = Invoke-CliJson -Arguments @('unignore-package', '--manager', '.NET Tool', '--package-id', 'dotnetsay')
    if ($unignore.status -ne 'success') {
        throw "unignore-package failed: $($unignore | ConvertTo-Json -Depth 8)"
    }

    $ignoredUpdates = Invoke-CliJson -Arguments @('list-ignored-updates')
    if (@($ignoredUpdates.ignoredUpdates | Where-Object { $_.packageId -eq 'dotnetsay' }).Count -ne 0) {
        throw "unignore-package did not remove dotnetsay from ignored updates"
    }

    Write-Host ' - update package to latest'
    $update = Invoke-CliJson -Arguments @('update-package', '--manager', '.NET Tool', '--package-id', 'dotnetsay', '--version', $latestDotnetsayVersion)
    if ($update.status -ne 'success') {
        throw "update-package failed: $($update | ConvertTo-Json -Depth 8)"
    }

    Write-Host ' - wait for updated version'
    $installedAfterUpdate = Wait-ForCliCondition `
        -Arguments @('list-installed', '--manager', '.NET Tool') `
        -FailureMessage 'list-installed did not include an updated dotnetsay version after update' `
        -Condition {
            param($response)
            @($response.packages | Where-Object { $_.id -eq 'dotnetsay' -and $_.version -eq $latestDotnetsayVersion }).Count -gt 0
        }
    $updatedDotnetsay = @($installedAfterUpdate.packages | Where-Object { $_.id -eq 'dotnetsay' })

    Write-Host ' - uninstall package'
    $uninstall = Invoke-CliJson -Arguments @('uninstall-package', '--manager', '.NET Tool', '--package-id', 'dotnetsay', '--scope', 'Global')
    if ($uninstall.status -ne 'success') {
        throw "uninstall-package failed: $($uninstall | ConvertTo-Json -Depth 8)"
    }

    Write-Host ' - wait for uninstall cleanup'
    $installedAfterUninstall = Wait-ForCliCondition `
        -Arguments @('list-installed', '--manager', '.NET Tool') `
        -FailureMessage 'dotnetsay still appears in list-installed after uninstall' `
        -Condition {
            param($response)
            @($response.packages | Where-Object { $_.id -eq 'dotnetsay' }).Count -eq 0
        }
    $remainingDotnetsay = @($installedAfterUninstall.packages | Where-Object { $_.id -eq 'dotnetsay' })

    Write-Stage 'History and manager logs'
    $operationHistory = Invoke-CliJson -Arguments @('get-operation-history')
    if ($null -eq $operationHistory.history) {
        throw "get-operation-history did not return a history payload"
    }
    if (
        @($operationHistory.history).Count -gt 0 -and
        @($operationHistory.history | Where-Object { -not [string]::IsNullOrWhiteSpace($_.content) }).Count -eq 0
    ) {
        throw "get-operation-history returned entries without readable content"
    }

    $managerLog = Wait-ForCliCondition `
        -Arguments @('get-manager-log', '--manager', '.NET Tool', '--verbose') `
        -FailureMessage 'get-manager-log did not capture .NET Tool task output for dotnetsay' `
        -Condition {
            param($response)
            @(
                $response.managers |
                    Where-Object {
                        $_.displayName -eq '.NET Tool' -and
                        @(
                            $_.tasks |
                                Where-Object {
                                    @($_.lines | Where-Object { $_ -match 'dotnetsay' }).Count -gt 0
                                }
                        ).Count -gt 0
                    }
            ).Count -gt 0
        }

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
