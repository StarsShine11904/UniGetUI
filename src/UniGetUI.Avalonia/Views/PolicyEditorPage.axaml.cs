using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Platform.Storage;
using FluentAvalonia.UI.Controls;
using UniGetUI.Avalonia.Models;
using UniGetUI.Core.Data;
using UniGetUI.PackageEngine.Classes.Serializable;

namespace UniGetUI.Avalonia.Views;

public partial class PolicyEditorPage : UserControl
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = null,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    private static readonly Dictionary<string, string> ManagerAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Winget"] = "winget",
        ["WinGet"] = "winget",
        ["Scoop"] = "scoop",
        ["Chocolatey"] = "chocolatey",
        ["Npm"] = "npm",
        ["Pip"] = "pip",
        ["PowerShell"] = "powershell",
        ["PowerShell7"] = "powershell7",
        [".NET Tool"] = "dotnettool",
        ["DotNet"] = "dotnettool",
        ["Cargo"] = "cargo",
        ["vcpkg"] = "vcpkg",
        ["Homebrew"] = "homebrew",
    };

    private readonly ObservableCollection<PolicyPackageRule> _packages = [];
    private readonly ObservableCollection<PolicyTrustedCallerRule> _trustedCallers = [];
    private string? _currentFilePath;
    private bool _isDirty;

    public PolicyEditorPage()
    {
        InitializeComponent();
        SetupPackagesGrid();
        SetupTrustedCallersGrid();

        PackagesGrid.ItemsSource = _packages;
        TrustedCallersGrid.ItemsSource = _trustedCallers;

        _packages.CollectionChanged += CollectionChanged;
        _trustedCallers.CollectionChanged += CollectionChanged;

        ResetEditor();
    }

    private void SetupPackagesGrid()
    {
        PackagesGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Manager",
            Binding = new Binding(nameof(PolicyPackageRule.Manager)),
            Width = new DataGridLength(140),
        });
        PackagesGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Package ID",
            Binding = new Binding(nameof(PolicyPackageRule.Id)),
            Width = new DataGridLength(2, DataGridLengthUnitType.Star),
        });
        PackagesGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Source",
            Binding = new Binding(nameof(PolicyPackageRule.Source)),
            Width = new DataGridLength(1.2, DataGridLengthUnitType.Star),
        });
        PackagesGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Operations",
            Binding = new Binding(nameof(PolicyPackageRule.OperationsSummary)),
            Width = new DataGridLength(160),
        });
    }

    private void SetupTrustedCallersGrid()
    {
        TrustedCallersGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Path",
            Binding = new Binding(nameof(PolicyTrustedCallerRule.PathEquals)),
            Width = new DataGridLength(2, DataGridLengthUnitType.Star),
        });
        TrustedCallersGrid.Columns.Add(new DataGridTemplateColumn
        {
            Header = "Signature required",
            Width = new DataGridLength(140),
            CellTemplate = new FuncDataTemplate<PolicyTrustedCallerRule>((caller, _) =>
                new CheckBox
                {
                    IsChecked = caller.SignatureRequired,
                    IsEnabled = false,
                    HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Center,
                    VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center,
                }),
        });
    }

    private void CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        MarkDirty();
        UpdatePreview();
    }

    private void ResetEditor()
    {
        _packages.Clear();
        _trustedCallers.Clear();
        _currentFilePath = null;
        _isDirty = false;

        KindTextBox.Text = PolicyEditorConstants.PolicyKind;
        SchemaVersionTextBox.Text = PolicyEditorConstants.SchemaVersion;
        PolicyIdTextBox.Text = Guid.NewGuid().ToString();
        GeneratedByTextBox.Text = $"UniGetUI.Avalonia {CoreData.VersionName}";
        DefaultInstallCheckBox.IsChecked = true;
        DefaultUpdateCheckBox.IsChecked = true;
        AllowAnyVersionCheckBox.IsChecked = true;
        CaseSensitiveIdCheckBox.IsChecked = false;

        ClearPackageForm();
        ClearCallerForm();
        UpdateSummary();
        UpdatePreview();
        SetStatus("New policy ready");
    }

    private void ClearPackageForm()
    {
        ManagerComboBox.SelectedIndex = 0;
        PackageIdTextBox.Text = string.Empty;
        PackageSourceTextBox.Text = string.Empty;
        PackageInstallCheckBox.IsChecked = true;
        PackageUpdateCheckBox.IsChecked = true;
        PackagesGrid.SelectedItem = null;
    }

    private void ClearCallerForm()
    {
        CallerPathTextBox.Text = string.Empty;
        CallerSignatureCheckBox.IsChecked = false;
        TrustedCallersGrid.SelectedItem = null;
    }

    private PolicyEditorDocument BuildDocument(bool refreshTimestamp)
    {
        List<string> defaultOperations = [];
        if (DefaultInstallCheckBox.IsChecked == true)
        {
            defaultOperations.Add(PolicyEditorConstants.InstallOperation);
        }

        if (DefaultUpdateCheckBox.IsChecked == true)
        {
            defaultOperations.Add(PolicyEditorConstants.UpdateOperation);
        }

        PolicyEditorDocument document = new()
        {
            Kind = KindTextBox.Text?.Trim() ?? PolicyEditorConstants.PolicyKind,
            SchemaVersion = SchemaVersionTextBox.Text?.Trim() ?? PolicyEditorConstants.SchemaVersion,
            PolicyId = PolicyIdTextBox.Text?.Trim() ?? Guid.NewGuid().ToString(),
            GeneratedAtUtc = refreshTimestamp ? DateTime.UtcNow : DateTime.UtcNow,
            GeneratedBy = new PolicyGeneratedBy
            {
                Product = "UniGetUI.Avalonia",
                Version = CoreData.VersionName,
            },
            Defaults = new PolicyDefaults
            {
                AllowOperations = defaultOperations,
                AllowAnyVersion = AllowAnyVersionCheckBox.IsChecked == true,
                CaseSensitiveId = CaseSensitiveIdCheckBox.IsChecked == true,
            },
            TrustedCallers = _trustedCallers
                .Select(static caller => new PolicyTrustedCallerRule
                {
                    PathEquals = caller.PathEquals.Trim(),
                    SignatureRequired = caller.SignatureRequired,
                })
                .Where(static caller => !string.IsNullOrWhiteSpace(caller.PathEquals))
                .OrderBy(static caller => caller.PathEquals, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            Packages = _packages
                .Select(static package => new PolicyPackageRule
                {
                    Manager = package.Manager.Trim(),
                    Id = package.Id.Trim(),
                    Source = string.IsNullOrWhiteSpace(package.Source) ? null : package.Source.Trim(),
                    AllowOperations = package.AllowOperations
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)
                        .ToList(),
                })
                .Where(static package => !string.IsNullOrWhiteSpace(package.Manager) && !string.IsNullOrWhiteSpace(package.Id))
                .OrderBy(static package => package.Manager, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static package => package.Id, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static package => package.Source, StringComparer.OrdinalIgnoreCase)
                .ToList(),
        };

        return document;
    }

    private void UpdatePreview()
    {
        PolicyEditorDocument document = BuildDocument(refreshTimestamp: false);
        JsonPreviewTextBox.Text = JsonSerializer.Serialize(document, SerializerOptions);
        UpdateSummary(document);
    }

    private void UpdateSummary(PolicyEditorDocument? document = null)
    {
        document ??= BuildDocument(refreshTimestamp: false);

        SummaryFileText.Text = string.IsNullOrWhiteSpace(_currentFilePath)
            ? "Current file: not saved yet"
            : $"Current file: {_currentFilePath}";
        SummaryPackagesText.Text = $"Packages: {document.Packages.Count}";
        SummaryCallersText.Text = $"Trusted callers: {document.TrustedCallers.Count}";
        SummaryTimestampText.Text = $"Preview generated at: {DateTime.UtcNow:u}";
    }

    private void MarkDirty()
    {
        _isDirty = true;
    }

    private void SetStatus(string message)
    {
        EditorStatusText.Text = _isDirty ? $"{message} • unsaved changes" : message;
    }

    private string NormalizeManager(string manager)
    {
        string trimmed = manager.Trim();
        if (ManagerAliases.TryGetValue(trimmed, out string? canonical))
        {
            return canonical;
        }

        return trimmed.ToLowerInvariant().Replace(" ", string.Empty, StringComparison.Ordinal);
    }

    private string NormalizePackageId(string packageId)
    {
        string trimmed = packageId.Trim();
        return CaseSensitiveIdCheckBox.IsChecked == true ? trimmed : trimmed.ToLowerInvariant();
    }

    private string NormalizeSource(string source)
    {
        string trimmed = source.Trim();
        return trimmed.ToLowerInvariant();
    }

    private string BuildPackageKey(string manager, string packageId, string? source)
    {
        string normalizedManager = NormalizeManager(manager);
        string normalizedId = NormalizePackageId(packageId);
        string normalizedSource = string.IsNullOrWhiteSpace(source) ? string.Empty : NormalizeSource(source);
        return $"{normalizedManager}|{normalizedId}|{normalizedSource}";
    }

    private PolicyPackageRule CreatePackageRule(string manager, string packageId, string? source, bool allowInstall, bool allowUpdate)
    {
        List<string> operations = [];
        if (allowInstall)
        {
            operations.Add(PolicyEditorConstants.InstallOperation);
        }

        if (allowUpdate)
        {
            operations.Add(PolicyEditorConstants.UpdateOperation);
        }

        return new PolicyPackageRule
        {
            Manager = NormalizeManager(manager),
            Id = NormalizePackageId(packageId),
            Source = string.IsNullOrWhiteSpace(source) ? null : source.Trim(),
            AllowOperations = operations,
        };
    }

    private void UpsertPackage(PolicyPackageRule package)
    {
        string key = BuildPackageKey(package.Manager, package.Id, package.Source);
        PolicyPackageRule? existing = _packages.FirstOrDefault(existingPackage =>
            BuildPackageKey(existingPackage.Manager, existingPackage.Id, existingPackage.Source) == key);

        if (existing is null)
        {
            _packages.Add(package);
            SetStatus($"Added package rule for {package.Id}");
        }
        else
        {
            int index = _packages.IndexOf(existing);
            _packages[index] = package;
            SetStatus($"Updated package rule for {package.Id}");
        }

        SortPackages();
    }

    private void SortPackages()
    {
        List<PolicyPackageRule> ordered = _packages
            .OrderBy(static package => package.Manager, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static package => package.Id, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static package => package.Source, StringComparer.OrdinalIgnoreCase)
            .ToList();

        _packages.CollectionChanged -= CollectionChanged;
        _packages.Clear();
        foreach (PolicyPackageRule package in ordered)
        {
            _packages.Add(package);
        }
        _packages.CollectionChanged += CollectionChanged;

        MarkDirty();
        UpdatePreview();
    }

    private void UpsertCaller(PolicyTrustedCallerRule caller)
    {
        PolicyTrustedCallerRule? existing = _trustedCallers.FirstOrDefault(existingCaller =>
            string.Equals(existingCaller.PathEquals, caller.PathEquals, StringComparison.OrdinalIgnoreCase));

        if (existing is null)
        {
            _trustedCallers.Add(caller);
            SetStatus("Added trusted caller");
        }
        else
        {
            int index = _trustedCallers.IndexOf(existing);
            _trustedCallers[index] = caller;
            SetStatus("Updated trusted caller");
        }

        SortCallers();
    }

    private void SortCallers()
    {
        List<PolicyTrustedCallerRule> ordered = _trustedCallers
            .OrderBy(static caller => caller.PathEquals, StringComparer.OrdinalIgnoreCase)
            .ToList();

        _trustedCallers.CollectionChanged -= CollectionChanged;
        _trustedCallers.Clear();
        foreach (PolicyTrustedCallerRule caller in ordered)
        {
            _trustedCallers.Add(caller);
        }
        _trustedCallers.CollectionChanged += CollectionChanged;

        MarkDirty();
        UpdatePreview();
    }

    private async Task<string?> PickOpenFileAsync(string title, params string[] patterns)
    {
        TopLevel? topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null)
        {
            return null;
        }

        IReadOnlyList<IStorageFile> files = await topLevel.StorageProvider.OpenFilePickerAsync(
            new FilePickerOpenOptions
            {
                Title = title,
                AllowMultiple = false,
                FileTypeFilter =
                [
                    new FilePickerFileType("JSON files")
                    {
                        Patterns = patterns,
                    },
                ],
            });

        return files.Count > 0 ? files[0].Path.LocalPath : null;
    }

    private async Task<string?> PickSaveFileAsync(string title, string suggestedFileName)
    {
        TopLevel? topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null)
        {
            return null;
        }

        IStorageFile? file = await topLevel.StorageProvider.SaveFilePickerAsync(
            new FilePickerSaveOptions
            {
                Title = title,
                SuggestedFileName = suggestedFileName,
                DefaultExtension = "json",
                FileTypeChoices =
                [
                    new FilePickerFileType("JSON files")
                    {
                        Patterns = ["*.json"],
                    },
                ],
            });

        return file?.Path.LocalPath;
    }

    private async Task SaveToPathAsync(string filePath)
    {
        PolicyEditorDocument document = BuildDocument(refreshTimestamp: true);
        string json = JsonSerializer.Serialize(document, SerializerOptions);
        await File.WriteAllTextAsync(filePath, json);
        _currentFilePath = filePath;
        _isDirty = false;
        UpdatePreview();
        SetStatus($"Saved policy to {filePath}");
    }

    private void LoadDocument(PolicyEditorDocument document, string? filePath)
    {
        if (!string.Equals(document.Kind, PolicyEditorConstants.PolicyKind, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"Unsupported policy kind: {document.Kind}");
        }

        _packages.CollectionChanged -= CollectionChanged;
        _trustedCallers.CollectionChanged -= CollectionChanged;
        _packages.Clear();
        _trustedCallers.Clear();

        KindTextBox.Text = string.IsNullOrWhiteSpace(document.Kind) ? PolicyEditorConstants.PolicyKind : document.Kind;
        SchemaVersionTextBox.Text = string.IsNullOrWhiteSpace(document.SchemaVersion) ? PolicyEditorConstants.SchemaVersion : document.SchemaVersion;
        PolicyIdTextBox.Text = string.IsNullOrWhiteSpace(document.PolicyId) ? Guid.NewGuid().ToString() : document.PolicyId;
        GeneratedByTextBox.Text = $"{document.GeneratedBy.Product} {document.GeneratedBy.Version}";

        DefaultInstallCheckBox.IsChecked = document.Defaults.AllowOperations.Contains(PolicyEditorConstants.InstallOperation, StringComparer.OrdinalIgnoreCase);
        DefaultUpdateCheckBox.IsChecked = document.Defaults.AllowOperations.Contains(PolicyEditorConstants.UpdateOperation, StringComparer.OrdinalIgnoreCase);
        AllowAnyVersionCheckBox.IsChecked = document.Defaults.AllowAnyVersion;
        CaseSensitiveIdCheckBox.IsChecked = document.Defaults.CaseSensitiveId;

        foreach (PolicyPackageRule package in document.Packages)
        {
            _packages.Add(CreatePackageRule(
                package.Manager,
                package.Id,
                package.Source,
                package.AllowOperations.Contains(PolicyEditorConstants.InstallOperation, StringComparer.OrdinalIgnoreCase),
                package.AllowOperations.Contains(PolicyEditorConstants.UpdateOperation, StringComparer.OrdinalIgnoreCase)));
        }

        foreach (PolicyTrustedCallerRule caller in document.TrustedCallers)
        {
            if (!string.IsNullOrWhiteSpace(caller.PathEquals))
            {
                _trustedCallers.Add(new PolicyTrustedCallerRule
                {
                    PathEquals = caller.PathEquals.Trim(),
                    SignatureRequired = caller.SignatureRequired,
                });
            }
        }

        _packages.CollectionChanged += CollectionChanged;
        _trustedCallers.CollectionChanged += CollectionChanged;

        _currentFilePath = filePath;
        _isDirty = false;
        ClearPackageForm();
        ClearCallerForm();
        UpdatePreview();
        SetStatus(filePath is null ? "Policy loaded" : $"Loaded policy from {filePath}");
    }

    private void ImportBundleFromPath(string filePath)
    {
        string json = File.ReadAllText(filePath);
        JsonNode node = JsonNode.Parse(json) ?? throw new InvalidDataException("Bundle file is empty or invalid JSON");
        SerializableBundle bundle = new(node);

        int importedCount = 0;
        foreach (SerializablePackage package in bundle.packages)
        {
            PolicyPackageRule rule = CreatePackageRule(
                package.ManagerName,
                package.Id,
                package.Source,
                allowInstall: true,
                allowUpdate: true);
            UpsertPackage(rule);
            importedCount++;
        }

        ClearPackageForm();
        SetStatus($"Imported {importedCount} compatible bundle packages from {filePath}");
    }

    private void Metadata_Changed(object? sender, TextChangedEventArgs e)
    {
        MarkDirty();
        UpdatePreview();
        SetStatus("Metadata updated");
    }

    private void Defaults_Changed(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        MarkDirty();
        UpdatePreview();
        SetStatus("Defaults updated");
    }

    private void NewPolicy_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        ResetEditor();
    }

    private async void OpenPolicy_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        try
        {
            string? filePath = await PickOpenFileAsync("Open policy JSON", "*.json");
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return;
            }

            string json = await File.ReadAllTextAsync(filePath);
            PolicyEditorDocument? document = JsonSerializer.Deserialize<PolicyEditorDocument>(json, SerializerOptions);
            if (document is null)
            {
                throw new InvalidDataException("Policy file could not be deserialized");
            }

            LoadDocument(document, filePath);
        }
        catch (Exception ex)
        {
            SetStatus($"Open failed: {ex.Message}");
        }
    }

    private async void SavePolicy_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_currentFilePath))
            {
                string suggestedName = $"uniget-policy-{DateTime.UtcNow:yyyyMMdd-HHmmss}.json";
                string? filePath = await PickSaveFileAsync("Save policy JSON", suggestedName);
                if (string.IsNullOrWhiteSpace(filePath))
                {
                    return;
                }

                await SaveToPathAsync(filePath);
                return;
            }

            await SaveToPathAsync(_currentFilePath);
        }
        catch (Exception ex)
        {
            SetStatus($"Save failed: {ex.Message}");
        }
    }

    private async void SaveAsPolicy_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        try
        {
            string suggestedName = $"uniget-policy-{DateTime.UtcNow:yyyyMMdd-HHmmss}.json";
            string? filePath = await PickSaveFileAsync("Save policy JSON as", suggestedName);
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return;
            }

            await SaveToPathAsync(filePath);
        }
        catch (Exception ex)
        {
            SetStatus($"Save as failed: {ex.Message}");
        }
    }

    private async void ImportBundle_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        try
        {
            string? filePath = await PickOpenFileAsync("Import UniGetUI bundle", "*.json");
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return;
            }

            ImportBundleFromPath(filePath);
            MarkDirty();
            UpdatePreview();
        }
        catch (Exception ex)
        {
            SetStatus($"Bundle import failed: {ex.Message}");
        }
    }

    private void SavePackage_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(PackageIdTextBox.Text))
        {
            SetStatus("Package ID is required");
            return;
        }

        if (PackageInstallCheckBox.IsChecked != true && PackageUpdateCheckBox.IsChecked != true)
        {
            SetStatus("Select at least one operation for the package rule");
            return;
        }

        PolicyPackageRule package = CreatePackageRule(
            GetSelectedManager(),
            PackageIdTextBox.Text,
            PackageSourceTextBox.Text,
            PackageInstallCheckBox.IsChecked == true,
            PackageUpdateCheckBox.IsChecked == true);

        UpsertPackage(package);
        ClearPackageForm();
    }

    private string GetSelectedManager()
    {
        if (ManagerComboBox.SelectedItem is ComboBoxItem item)
        {
            return item.Content?.ToString() ?? "winget";
        }

        return "winget";
    }

    private void ClearPackageForm_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        ClearPackageForm();
        SetStatus("Package form cleared");
    }

    private void RemoveSelectedPackages_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        List<PolicyPackageRule> selected = PackagesGrid.SelectedItems.Cast<PolicyPackageRule>().ToList();
        if (selected.Count == 0)
        {
            SetStatus("Select one or more package rules to remove");
            return;
        }

        foreach (PolicyPackageRule package in selected)
        {
            _packages.Remove(package);
        }

        ClearPackageForm();
        SetStatus($"Removed {selected.Count} package rule(s)");
    }

    private void PackagesGrid_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (PackagesGrid.SelectedItem is not PolicyPackageRule package)
        {
            return;
        }

        SelectManager(package.Manager);
        PackageIdTextBox.Text = package.Id;
        PackageSourceTextBox.Text = package.Source ?? string.Empty;
        PackageInstallCheckBox.IsChecked = package.AllowOperations.Contains(PolicyEditorConstants.InstallOperation, StringComparer.OrdinalIgnoreCase);
        PackageUpdateCheckBox.IsChecked = package.AllowOperations.Contains(PolicyEditorConstants.UpdateOperation, StringComparer.OrdinalIgnoreCase);
    }

    private void SelectManager(string manager)
    {
        foreach (object? item in ManagerComboBox.Items)
        {
            if (item is ComboBoxItem comboBoxItem && string.Equals(comboBoxItem.Content?.ToString(), manager, StringComparison.OrdinalIgnoreCase))
            {
                ManagerComboBox.SelectedItem = comboBoxItem;
                return;
            }
        }

        ManagerComboBox.SelectedIndex = 0;
    }

    private async void BrowseCaller_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        string? filePath = await PickOpenFileAsync("Select trusted caller executable", "*.exe", "*.cmd", "*.bat");
        if (!string.IsNullOrWhiteSpace(filePath))
        {
            CallerPathTextBox.Text = filePath;
        }
    }

    private void SaveCaller_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(CallerPathTextBox.Text))
        {
            SetStatus("Trusted caller path is required");
            return;
        }

        PolicyTrustedCallerRule caller = new()
        {
            PathEquals = CallerPathTextBox.Text.Trim(),
            SignatureRequired = CallerSignatureCheckBox.IsChecked == true,
        };

        UpsertCaller(caller);
        ClearCallerForm();
    }

    private void RemoveSelectedCallers_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        List<PolicyTrustedCallerRule> selected = TrustedCallersGrid.SelectedItems.Cast<PolicyTrustedCallerRule>().ToList();
        if (selected.Count == 0)
        {
            SetStatus("Select one or more trusted callers to remove");
            return;
        }

        foreach (PolicyTrustedCallerRule caller in selected)
        {
            _trustedCallers.Remove(caller);
        }

        ClearCallerForm();
        SetStatus($"Removed {selected.Count} trusted caller rule(s)");
    }

    private void TrustedCallersGrid_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (TrustedCallersGrid.SelectedItem is not PolicyTrustedCallerRule caller)
        {
            return;
        }

        CallerPathTextBox.Text = caller.PathEquals;
        CallerSignatureCheckBox.IsChecked = caller.SignatureRequired;
    }

    private void RefreshPreview_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        UpdatePreview();
        SetStatus("Preview refreshed");
    }
}
