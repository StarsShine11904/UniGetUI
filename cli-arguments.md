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
| `--automation get-version` | Reads the local automation service build number through the background API | 2026.1+ |
| `--automation get-updates` | Reads the currently available updates through the local automation service and returns structured JSON | 2026.1+ |
| `--automation list-managers` | Lists package managers, readiness, executable metadata, and automation-relevant capability flags | 2026.1+ |
| `--automation list-sources [--manager name]` | Lists known and configured sources, optionally filtered to a single manager | 2026.1+ |
| `--automation add-source --manager name --source-name name [--source-url url]` | Adds a known or custom source through the automation service | 2026.1+ |
| `--automation remove-source --manager name --source-name name [--source-url url]` | Removes a source through the automation service | 2026.1+ |
| `--automation list-settings` | Lists non-sensitive settings with their current boolean/string state | 2026.1+ |
| `--automation get-setting --key key` | Reads a single non-sensitive setting through the automation service | 2026.1+ |
| `--automation set-setting --key key (--enabled true|false \| --value text)` | Sets a boolean or string setting through the automation service | 2026.1+ |
| `--automation clear-setting --key key` | Clears a string-backed setting through the automation service | 2026.1+ |
| `--automation reset-settings` | Resets non-secure settings while preserving the active automation session token | 2026.1+ |
| `--automation get-app-log [--level n]` | Reads the UniGetUI application log as structured JSON, with optional severity filtering | 2026.1+ |
| `--automation get-operation-history` | Reads the persisted operation history shown by the log/history UI surfaces | 2026.1+ |
| `--automation get-manager-log [--manager name] [--verbose]` | Reads manager task logs, optionally for one manager and with verbose subprocess/stdin/stdout detail | 2026.1+ |
| `--automation list-installed --manager name` | Lists installed packages for the selected manager through the automation service and returns structured JSON | 2026.1+ |
| `--automation search-packages --manager name --query text [--max-results n]` | Searches packages through the automation service and returns structured JSON | 2026.1+ |
| `--automation package-details --manager name --package-id id` | Fetches the package-details payload currently exposed through the automation layer | 2026.1+ |
| `--automation package-versions --manager name --package-id id` | Lists installable versions for a package when the manager supports custom versions | 2026.1+ |
| `--automation install-package --manager name --package-id id [--version v] [--scope scope] [--pre-release]` | Installs a package through the automation service and waits for completion | 2026.1+ |
| `--automation open-window` | Asks the running UniGetUI instance to show the main window | 2026.1+ |
| `--automation open-updates` | Asks the running UniGetUI instance to show the Updates page | 2026.1+ |
| `--automation show-package --package-id id --package-source source` | Opens the package details flow for the specified package | 2026.1+ |
| `--automation list-ignored-updates` | Lists ignored update rules tracked by UniGetUI | 2026.1+ |
| `--automation ignore-package --manager name --package-id id [--version v]` | Adds an ignored-update rule for a package and refreshes the updates view | 2026.1+ |
| `--automation unignore-package --manager name --package-id id [--version v]` | Removes an ignored-update rule for a package and refreshes the updates view | 2026.1+ |
| `--automation update-all` | Queues updates for all packages currently shown as upgradable | 2026.1+ |
| `--automation update-manager --manager name` | Queues updates for all packages handled by the specified manager | 2026.1+ |
| `--automation update-package --manager name --package-id id [--version v]` | Updates a specific package through the automation service and waits for completion | 2026.1+ |
| `--automation uninstall-package --manager name --package-id id [--scope scope]` | Uninstalls a package through the automation service and waits for completion | 2026.1+ |
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
- Current agent-oriented command coverage includes status/version, manager/source inspection, settings inspection and mutation, app/history/manager log inspection, package search/details/version listing, ignored-update management, and package install/update/uninstall flows.

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
