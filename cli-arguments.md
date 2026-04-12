# UniGetUI Command-line parameters

| Parameter ____________________________________ | Description | Compatible versions  ______________  |
| ---------------------- | ---------- | ------- |
| `--daemon` | Start UniGetUI without spawning a new window. UniGetUI will run minimized on the system tray. UniGetUI is called with this parameter when launched at startup. **Autostart UniGetUI in the notifications area must be enabled for this parameter to work.** | 1.0+ |
| `--welcome` | Shows the user the Setup Wizard | up to 2.2.0 |
| `--updateapps` | Force enable automatic installation of available updates | 1.6.0+ |
| `--report-all-errors` | Will force UniGetUI to show the error report page on any crash when loading | 3.0.0+ |
| `--uninstall-unigetui` | Will unregister UniGetUI from the notification panel, and silently quit | from 3.1.0 to 3.1.8 |
| `--migrate-wingetui-to-unigetui` | Will migrate WingetUI data folders and shortcuts to UniGetUI (if possible), and silently quit | 3.1.0+ |
| `UniGetUI.exe file` | Provided that the file is a valid bundle, will load the bundle into the Package Bundles page. Compatible bundle files include the following extensions: `.ubundle`, `.json`, `.yaml`, `.xml` | 3.1.2+ |
| `--help` | Opens this page | 3.2.0+ |
| `--import-settings file` | Imports UniGetUI settings from json file _file_. The file must exist. The old settings will be lost* | 3.2.0+ |
| `--export-settings file` |  Exports UniGetUI settings to json file _file_. The file will be created or overwritten* | 3.2.0+ |
| `--[enable\|disable]-setting key` | Enables/disables the boolean setting _key<sup>1</sup>_ | 3.2.0+ |
| `--set-setting-value key value` | Sets the value _value_ to the non-boolean setting _key<sup>1</sup>_. To clear a non-boolean setting, `--disable-setting` can be used* | 3.2.0+ |
| `--no-corrupt-dialog` | Will show a verbose error message (the error report) instead of a simplified message dialog | 3.2.1+ |
| `--[enable\|disable]-secure-setting-for-user username key` | Enables/disables the given secure setting for the given key<sup>2</sup> and username. Requires administrator rights.  | 3.2.1+ | 
| `--[enable\|disable]-secure-setting key` | Enables/disables the given secure setting<sup>2</sup> for current user. This will generate a UAC prompt  | 3.2.1+ | 
| `--headless` | Starts the Avalonia host as a pure automation daemon with **no UI** and no requirement for a working graphical environment. Compatible with `--background-api-*` transport arguments. | 2026.1+ |
| `--automation status` | Queries the local automation service and returns machine-readable status, including the configured background API transport | 2026.1+ |
| `--automation get-app-state` | Returns app/session automation state including headless mode, current page when a window exists, and which app-level actions are supported by the current host | 2026.1+ |
| `--automation show-app` | Asks the running UniGetUI instance to show and focus its main window when a UI session exists | 2026.1+ |
| `--automation navigate-app --page {discover\|updates\|installed\|bundles\|settings\|managers\|own-log\|manager-log\|operation-history\|help\|release-notes\|about} [--manager name] [--help-attachment path]` | Navigates the running UI session to a top-level destination, with optional manager-specific or help-page context where supported | 2026.1+ |
| `--automation quit-app` | Gracefully shuts down the current UniGetUI session, including the headless automation daemon | 2026.1+ |
| `--automation get-version` | Reads the local automation service build number through the background API | 2026.1+ |
| `--automation get-updates` | Reads the currently available updates through the local automation service and returns structured JSON | 2026.1+ |
| `--automation list-managers` | Lists package managers, readiness, executable metadata, and automation-relevant capability flags | 2026.1+ |
| `--automation get-manager-maintenance --manager name` | Returns manager-specific maintenance metadata, supported maintenance actions, executable-path candidates, and convenience state for manager-only settings such as bundled/system toggles and vcpkg triplets | 2026.1+ |
| `--automation reload-manager --manager name` | Re-initializes one manager and returns the refreshed maintenance payload | 2026.1+ |
| `--automation set-manager-executable --manager name --path path` | Sets a custom executable override for one manager when secure settings allow custom manager paths, then reloads that manager | 2026.1+ |
| `--automation clear-manager-executable --manager name` | Clears a custom executable override for one manager and reloads that manager | 2026.1+ |
| `--automation run-manager-action --manager name --action action [--confirm]` | Runs an explicit manager-maintenance action. Current actions are `repair-winget`, `install-scoop`, `uninstall-scoop`, and `cleanup-scoop`; system-changing actions require `--confirm` | 2026.1+ |
| `--automation list-sources [--manager name]` | Lists known and configured sources, optionally filtered to a single manager | 2026.1+ |
| `--automation add-source --manager name --source-name name [--source-url url]` | Adds a known or custom source through the automation service | 2026.1+ |
| `--automation remove-source --manager name --source-name name [--source-url url]` | Removes a source through the automation service | 2026.1+ |
| `--automation list-settings` | Lists non-sensitive settings with their current boolean/string state | 2026.1+ |
| `--automation list-secure-settings [--user name]` | Lists all secure settings for the current user or a specified user in machine-readable form | 2026.1+ |
| `--automation get-secure-setting --key key [--user name]` | Reads one secure setting for the current user or a specified user | 2026.1+ |
| `--automation set-secure-setting --key key --enabled true\|false [--user name]` | Enables or disables one secure setting for the current user or a specified user | 2026.1+ |
| `--automation get-setting --key key` | Reads a single non-sensitive setting through the automation service | 2026.1+ |
| `--automation set-setting --key key (--enabled true|false \| --value text)` | Sets a boolean or string setting through the automation service | 2026.1+ |
| `--automation clear-setting --key key` | Clears a string-backed setting through the automation service | 2026.1+ |
| `--automation reset-settings` | Resets non-secure settings while preserving the active automation session token | 2026.1+ |
| `--automation set-manager-enabled --manager name --enabled true\|false` | Enables or disables one package manager and reloads it immediately | 2026.1+ |
| `--automation set-manager-update-notifications --manager name --enabled true\|false` | Enables or suppresses update notifications for one package manager | 2026.1+ |
| `--automation list-desktop-shortcuts` | Lists tracked desktop shortcuts, their current keep/delete/unknown verdicts, and whether each shortcut still exists on disk | 2026.1+ |
| `--automation set-desktop-shortcut --path path --status {keep\|delete}` | Marks a tracked shortcut to be kept or deleted; `delete` also removes the shortcut from disk when present | 2026.1+ |
| `--automation reset-desktop-shortcut --path path` | Clears the stored verdict for one tracked desktop shortcut | 2026.1+ |
| `--automation reset-desktop-shortcuts` | Clears all stored desktop-shortcut verdicts | 2026.1+ |
| `--automation get-app-log [--level n]` | Reads the UniGetUI application log as structured JSON, with optional severity filtering | 2026.1+ |
| `--automation get-operation-history` | Reads the persisted operation history shown by the log/history UI surfaces | 2026.1+ |
| `--automation get-manager-log [--manager name] [--verbose]` | Reads manager task logs, optionally for one manager and with verbose subprocess/stdin/stdout detail | 2026.1+ |
| `--automation get-backup-status` | Reads local-backup settings, resolved backup output metadata, and current GitHub cloud-auth/device-flow state | 2026.1+ |
| `--automation create-local-backup` | Creates a local `.ubundle` backup using the current backup settings and returns the written path | 2026.1+ |
| `--automation start-github-sign-in [--launch-browser]` | Starts a headless-friendly GitHub device-flow sign-in for cloud backup and returns the verification URI and user code | 2026.1+ |
| `--automation complete-github-sign-in` | Completes the pending GitHub device-flow sign-in after the user authorizes the device code | 2026.1+ |
| `--automation sign-out-github` | Clears the stored GitHub cloud-backup session token | 2026.1+ |
| `--automation list-cloud-backups` | Lists the cloud backups stored in the authenticated GitHub backup gist | 2026.1+ |
| `--automation create-cloud-backup` | Uploads the current installed-package backup bundle to the authenticated GitHub backup gist | 2026.1+ |
| `--automation download-cloud-backup --key name` | Downloads one cloud backup as raw bundle content | 2026.1+ |
| `--automation restore-cloud-backup --key name [--append]` | Downloads a cloud backup and imports it into the current in-memory bundle | 2026.1+ |
| `--automation get-bundle` | Reads the current in-memory package bundle as structured JSON, including compatibility and selected install-version metadata | 2026.1+ |
| `--automation reset-bundle` | Clears the current in-memory package bundle | 2026.1+ |
| `--automation import-bundle (--path path \| --content text) [--format {ubundle\|json\|yaml\|xml}] [--append]` | Loads bundle content from a file path or raw content, optionally appending instead of replacing the current bundle | 2026.1+ |
| `--automation export-bundle [--path path]` | Serializes the current in-memory bundle and optionally writes it to a `.ubundle` or `.json` file | 2026.1+ |
| `--automation add-bundle-package --package-id id [--manager name] [--package-source source] [--version v] [--scope scope] [--pre-release] [--selection {search\|installed\|updates\|auto}]` | Resolves a package and adds it to the current bundle with the requested install options | 2026.1+ |
| `--automation remove-bundle-package --package-id id [--manager name] [--package-source source] [--version v]` | Removes matching package entries from the current bundle | 2026.1+ |
| `--automation install-bundle [--include-installed true\|false] [--elevated true\|false] [--interactive true\|false] [--skip-hash true\|false]` | Installs the current bundle through the automation service and returns per-package results | 2026.1+ |
| `--automation list-installed --manager name` | Lists installed packages for the selected manager through the automation service and returns structured JSON | 2026.1+ |
| `--automation search-packages --manager name --query text [--max-results n]` | Searches packages through the automation service and returns structured JSON | 2026.1+ |
| `--automation package-details --manager name --package-id id` | Fetches the package-details payload currently exposed through the automation layer | 2026.1+ |
| `--automation package-versions --manager name --package-id id` | Lists installable versions for a package when the manager supports custom versions | 2026.1+ |
| `--automation install-package --manager name --package-id id [--version v] [--scope scope] [--pre-release] [--elevated true\|false] [--interactive true\|false] [--skip-hash true\|false] [--architecture value] [--location path]` | Installs a package through the automation service and waits for completion, honoring the same core install options exposed by the UI | 2026.1+ |
| `--automation download-package --manager name --package-id id --output path` | Downloads a package installer or artifact to the specified file or directory and returns the resolved saved path | 2026.1+ |
| `--automation reinstall-package --manager name --package-id id [--version v] [--scope scope] [--pre-release] [--elevated true\|false] [--interactive true\|false] [--skip-hash true\|false] [--architecture value] [--location path]` | Re-runs package installation for an installed package using the requested install options | 2026.1+ |
| `--automation open-window` | Legacy alias for `--automation show-app` | 2026.1+ |
| `--automation open-updates` | Legacy alias for `--automation navigate-app --page updates` | 2026.1+ |
| `--automation show-package --package-id id --package-source source` | Opens the package details flow for the specified package | 2026.1+ |
| `--automation list-ignored-updates` | Lists ignored update rules tracked by UniGetUI | 2026.1+ |
| `--automation ignore-package --manager name --package-id id [--version v]` | Adds an ignored-update rule for a package and refreshes the updates view | 2026.1+ |
| `--automation unignore-package --manager name --package-id id [--version v]` | Removes an ignored-update rule for a package and refreshes the updates view | 2026.1+ |
| `--automation update-all` | Queues updates for all packages currently shown as upgradable | 2026.1+ |
| `--automation update-manager --manager name` | Queues updates for all packages handled by the specified manager | 2026.1+ |
| `--automation update-package --manager name --package-id id [--version v] [--scope scope] [--pre-release] [--elevated true\|false] [--interactive true\|false] [--skip-hash true\|false] [--architecture value] [--location path]` | Updates a specific package through the automation service and waits for completion | 2026.1+ |
| `--automation uninstall-package --manager name --package-id id [--scope scope] [--remove-data true\|false] [--elevated true\|false] [--interactive true\|false]` | Uninstalls a package through the automation service and waits for completion | 2026.1+ |
| `--automation uninstall-then-reinstall-package --manager name --package-id id [--version v] [--scope scope] [--pre-release] [--remove-data true\|false] [--elevated true\|false] [--interactive true\|false] [--skip-hash true\|false] [--architecture value] [--location path]` | Uninstalls an installed package and then immediately reinstalls it through the shared operation pipeline | 2026.1+ |
| `--background-api-transport {tcp\|named-pipe}` | Selects which local HTTP transport UniGetUI uses for the background API when the app starts | 2026.1+ |
| `--background-api-port port` | Overrides the localhost TCP port used by the background API when `--background-api-transport tcp` is active | 2026.1+ |
| `--background-api-pipe-name name` | Overrides the Windows named pipe name used by the background API when `--background-api-transport named-pipe` is active | 2026.1+ |
| `--transport {tcp\|named-pipe}` | Overrides the client-side automation transport used by `--automation ...` commands | 2026.1+ |
| `--tcp-port port` | Overrides the client-side localhost TCP port used by `--automation ...` commands | 2026.1+ |
| `--pipe-name name` | Overrides the client-side named pipe used by `--automation ...` commands | 2026.1+ |

1. See the available list of setting keys [here](https://github.com/Devolutions/UniGetUI/blob/fc98f312a72b80e14a8ac10687f4fc506a5c9cc4/src/UniGetUI.Core.Settings/SettingsEngine_Names.cs#L5)
2. See the available list of secure settings keys [here](https://github.com/Devolutions/UniGetUI/blob/fc98f312a72b80e14a8ac10687f4fc506a5c9cc4/src/UniGetUI.Core.SecureSettings/SecureSettings.cs#L10)


\*After modifying the settings, you must ensure that any running instance of UniGetUI is restarted for the changes to take effect

## Headless automation daemon and cross-platform CLI

- `dotnet src\UniGetUI.Avalonia\bin\Release\net10.0\UniGetUI.Avalonia.dll --headless` starts the local automation daemon without opening any window or requiring a graphical desktop session.
- `dotnet src\UniGetUI.Cli\bin\Release\net10.0\UniGetUI.Cli.dll <command>` is the cross-platform CLI wrapper for the automation service. It automatically prepends `--automation`, so `UniGetUI.Cli status` and `UniGetUI.Cli search-packages --manager ".NET Tool" --query dotnetsay` work directly.
- Current agent-oriented command coverage includes app/session lifecycle inspection and shutdown, manager/source inspection plus manager enablement, notification suppression, manager-maintenance and executable-path control, settings and secure-settings inspection/mutation, desktop-shortcut state management, app/history/manager log inspection, local backup creation and GitHub cloud-backup/auth flows, current bundle inspection/import/export/add/remove/install flows, package search/details/version listing, ignored-update management, and package install/update/uninstall flows.

<br><br>
# `unigetui://` deep link
On a system where UniGetUI 3.1.2+ is installed, the following deep links can be used to communicate with UniGetUI:

| Parameter                                           | Description |
| --------------------------------------------------- | ---------- |
| `unigetui://showPackage?id={}&managerName={}&sourceName={}` | Show the Package Details page with the provided package. <br>The parameters `id`, `managerName` and `sourceName` are<br> required and cannot be empty |
| `unigetui://showUniGetUI` | Shows UniGetUI and brings the window to the front |
| `unigetui://showDiscoverPage` | Shows UniGetUI and loads the Discover page | 
| `unigetui://showUpdatesPage` | Shows UniGetUI and loads the Updates page | 
| `unigetui://showInstalledPage` | Shows UniGetUI and loads the Installed page | 

<br><br>

# Installer command-line parameters 
The installer is inno-setup based. It supports [all Inno Setup command-line parameters](https://jrsoftware.org/ishelp/index.php?topic=setupcmdline), as well as the following custom ones:

| Parameter                                           | Description |
| --------------------------------------------------- | ---------- |
| `/NoAutoStart` | Will not launch UniGetUI after installation |
| `/NoRunOnStartup` | Will not register UniGetUI to start minimized at login (v3.1.6+) |
| `/NoVCRedist` | Will not install MS Visual C++ Redistributable x64 (v3.1.2+) |
| `/NoEdgeWebView` | Will not install Microsoft Edge WebView Runtime (v3.1.2+) |
| `/NoChocolatey` | Do NOT install chocolatey within UniGetUI | 
| `/EnableSystemChocolatey` | Force UniGetUI to use system chocolatey |
| `/NoWinGet` | Do NOT install WinGet and Microsoft.WinGet.Client if not installed **(not recommended)** | 
| `/ALLUSERS` | Will force the installer to install per-machine (requires administrator privileges) |
| `/CURRENTUSER` | Will force the installer to install per-user | 
