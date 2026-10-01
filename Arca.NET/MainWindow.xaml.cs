using Arca.Core.Entities;
using Arca.Core.Interfaces;
using Arca.Core.Security;
using Arca.Core.Services;
using Arca.NET.Controls;
using Arca.NET.Models;
using Arca.NET.Services;
using System.Collections.ObjectModel;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Application = System.Windows.Application;
using Button = System.Windows.Controls.Button;
using Clipboard = System.Windows.Clipboard;

namespace Arca.NET;

public partial class MainWindow : Window
{
    private readonly byte[] _derivedKey;
    private readonly IVaultRepository _vaultRepository;
    private readonly IAesGcmService _aesGcmService;
    private readonly IKeyDerivationService _keyDerivationService;
    private readonly EmbeddedSecretServer _secretServer;
    private readonly VaultExportService _exportService = new();
    private readonly AutoBackupService _autoBackupService = new();
    private readonly SettingsService _settingsService = new();

    private ObservableCollection<SecretEntry> _secrets = [];
    private ObservableCollection<ApiKeyEntry> _apiKeys = [];
    private ObservableCollection<FolderItem> _folders = [];
    private ObservableCollection<SecretDisplayItem> _displaySecrets = [];
    private ObservableCollection<ApiKeyTreeNode> _apiKeyTreeNodes = [];
    private bool _isTreeExpanded = true;

    private readonly HashSet<string> _knownFolders = new(StringComparer.OrdinalIgnoreCase);
    private SecretEntry? _editingSecret;
    private SecretEntry? _movingSecret;
    private string? _currentFolderKey; // null = Todos, "" = Sin carpeta, "Nombre" = Carpeta especifica

    public MainWindow(
        byte[] derivedKey,
        IVaultRepository vaultRepository,
        IAesGcmService aesGcmService,
        IKeyDerivationService keyDerivationService)
    {
        InitializeComponent();
        ApplyInitialSettings();
        LocalizationService.LanguageChanged += (_, _) => OnLanguageChanged();

        var v = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        var versionText = v != null ? $"v{v.Major}.{v.Minor}.{v.Build}" : "";
        txtHeaderVersion.Text = versionText;
        txtStatusVersion.Text = versionText;
        txtAboutVersion.Text = versionText;

        _derivedKey = derivedKey;
        _vaultRepository = vaultRepository;
        _aesGcmService = aesGcmService;
        _keyDerivationService = keyDerivationService;
        _secretServer = new EmbeddedSecretServer();

        _secretServer.ApiKeyUsed += OnApiKeyUsed;

        Loaded += MainWindow_Loaded;
        StateChanged += MainWindow_StateChanged;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        NotificationService.Initialize(notificationsContainer);
        DialogService.Initialize(dialogContainer);

        LoadKnownFolders();
        await LoadSecretsAsync();
        await LoadApiKeysAsync();

        _secretServer.UpdateSecrets(_secrets);
        _secretServer.UpdateApiKeys(_apiKeys);
        _secretServer.RequireAuthentication = _apiKeys.Count > 0;
        _secretServer.Start();

        UpdateStatusBar();
    }

    private void MainWindow_StateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized)
        {
            if (Application.Current is App app)
            {
                app.MinimizeToTray();
            }
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _secretServer.Stop();
        _secretServer.Dispose();
        base.OnClosed(e);
    }

    private void UpdateStatusBar()
    {
        var authStatus = _secretServer.RequireAuthentication ? "🔐" : "⚠️";
        var baseStatus = LocalizationService.GetString("Status_VaultActive", "Baúl activo - Servidor SDK listo");
        txtStatus.Text = $"{baseStatus} {authStatus}";

        if (!_secretServer.RequireAuthentication && _apiKeys.Count == 0)
        {
            var noKeys = LocalizationService.GetString("Status_NoApiKeys", "(Sin API Keys - Acceso libre)");
            txtStatus.Text += $" {noKeys}";
        }
    }

    private async void OnApiKeyUsed(object? sender, string keyHash)
    {
        await Dispatcher.InvokeAsync(async () =>
        {
            var apiKey = _apiKeys.FirstOrDefault(k => k.KeyHash == keyHash);
            if (apiKey != null)
            {
                var index = _apiKeys.IndexOf(apiKey);
                _apiKeys[index] = apiKey with { LastUsedAt = DateTime.Now };
                await SaveApiKeysAsync();
            }
        });
    }

    #region Known Folders Persistence

    private string GetFoldersFilePath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var dir = Path.Combine(appData, "Arca");
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
        return Path.Combine(dir, "folders.json");
    }

    private void LoadKnownFolders()
    {
        try
        {
            var path = GetFoldersFilePath();
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                var list = JsonSerializer.Deserialize<List<string>>(json);
                if (list != null)
                {
                    foreach (var f in list.Where(s => !string.IsNullOrWhiteSpace(s)))
                    {
                        _knownFolders.Add(f.Trim());
                    }
                }
            }
        }
        catch { }
    }

    private void SaveKnownFolders()
    {
        try
        {
            var path = GetFoldersFilePath();
            var json = JsonSerializer.Serialize(_knownFolders.ToList());
            File.WriteAllText(path, json);
        }
        catch { }
    }

    #endregion

    #region Secrets & Folders Management

    private async Task LoadSecretsAsync()
    {
        try
        {
            var secrets = await _vaultRepository.LoadSecretsAsync(_derivedKey);
            _secrets = new ObservableCollection<SecretEntry>(secrets);

            foreach (var s in _secrets.Where(x => !string.IsNullOrWhiteSpace(x.Folder)))
            {
                _knownFolders.Add(s.Folder!.Trim());
            }
            SaveKnownFolders();

            _secretServer.UpdateSecrets(_secrets);

            RefreshFolders();
            RefreshList();
            UpdateSecretCount();

            if (Application.Current is App app)
            {
                app.UpdateSecretCount(_secrets.Count);
            }
        }
        catch (Exception ex)
        {
            NotificationService.ShowError("Error", $"Error loading secrets: {ex.Message}");
        }
    }

    private async Task LoadApiKeysAsync()
    {
        try
        {
            var apiKeys = await _vaultRepository.LoadApiKeysAsync(_derivedKey);
            _apiKeys = new ObservableCollection<ApiKeyEntry>(apiKeys);

            _secretServer.UpdateApiKeys(_apiKeys);
            UpdateStatusBar();
        }
        catch (Exception ex)
        {
            NotificationService.ShowError("Error", $"Error loading API keys: {ex.Message}");
        }
    }

    private async Task SaveSecretsAsync()
    {
        try
        {
            await _vaultRepository.SaveSecretsAsync(_secrets, _derivedKey);
            _secretServer.UpdateSecrets(_secrets);
            RefreshFolders();
            _autoBackupService.CreateSnapshot();
        }
        catch (Exception ex)
        {
            NotificationService.ShowError("Error", $"Error saving secrets: {ex.Message}");
        }
    }

    private async Task SaveApiKeysAsync()
    {
        try
        {
            await _vaultRepository.SaveApiKeysAsync(_apiKeys, _derivedKey);
            _secretServer.UpdateApiKeys(_apiKeys);
            UpdateStatusBar();
        }
        catch (Exception ex)
        {
            NotificationService.ShowError("Error", $"Error saving API keys: {ex.Message}");
        }
    }

    private void OnLanguageChanged()
    {
        UpdateSecretCount();
        RefreshFolders();
        RefreshList();
        UpdateStatusBar();
    }

    private void UpdateSecretCount()
    {
        var unit = _secrets.Count == 1 
            ? LocalizationService.GetString("Status_SecretSingular", "secreto")
            : LocalizationService.GetString("Status_SecretPlural", "secretos");
        txtSecretCount.Text = $"{_secrets.Count} {unit}";
    }

    private void RefreshFolders()
    {
        var totalCount = _secrets.Count;
        var unassignedCount = _secrets.Count(s => string.IsNullOrWhiteSpace(s.Folder));

        // Asegurar que todos los folders usados en secretos estan en _knownFolders
        foreach (var s in _secrets.Where(x => !string.IsNullOrWhiteSpace(x.Folder)))
        {
            _knownFolders.Add(s.Folder!.Trim());
        }

        var folderList = _knownFolders.OrderBy(f => f).ToList();

        _folders = new ObservableCollection<FolderItem>
        {
            new FolderItem
            {
                Name = LocalizationService.GetString("Folder_All", "Todos los secretos"),
                FolderKey = null,
                Count = totalCount,
                Icon = "📁",
                IsSpecial = true,
                IsSelected = _currentFolderKey == null
            },
            new FolderItem
            {
                Name = LocalizationService.GetString("Folder_NoFolder", "Sin carpeta / Raíz"),
                FolderKey = "",
                Count = unassignedCount,
                Icon = "📂",
                IsSpecial = true,
                IsSelected = _currentFolderKey == ""
            }
        };

        foreach (var folderName in folderList)
        {
            var count = _secrets.Count(s => string.Equals(s.Folder, folderName, StringComparison.OrdinalIgnoreCase));
            _folders.Add(new FolderItem
            {
                Name = folderName,
                FolderKey = folderName,
                Count = count,
                Icon = "🗂️",
                IsSpecial = false,
                IsSelected = string.Equals(_currentFolderKey, folderName, StringComparison.OrdinalIgnoreCase)
            });
        }

        lstFolders.ItemsSource = _folders;
    }

    private void FolderItem_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { Tag: FolderItem item })
        {
            _currentFolderKey = item.FolderKey;

            foreach (var f in _folders)
            {
                f.IsSelected = f == item;
            }

            if (item.FolderKey == null)
            {
                txtActiveFolderTitle.Text = "📁 Todos los secretos";
            }
            else if (item.FolderKey == "")
            {
                txtActiveFolderTitle.Text = "📂 Sin carpeta / Raíz";
            }
            else
            {
                txtActiveFolderTitle.Text = $"🗂️ {item.Name}";
            }

            RefreshList();
        }
    }

    private async void DeleteFolderButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: FolderItem folder } && !folder.IsSpecial && folder.FolderKey != null)
        {
            var secretsInFolder = _secrets.Where(s => string.Equals(s.Folder, folder.FolderKey, StringComparison.OrdinalIgnoreCase)).ToList();

            if (secretsInFolder.Count > 0)
            {
                var confirmed = await DialogService.ConfirmDangerousAsync(
                    "Eliminar Carpeta",
                    $"La carpeta '{folder.Name}' contiene {secretsInFolder.Count} secreto(s).\n\n¿Deseas eliminar la carpeta y mover sus secretos a 'Sin carpeta'?",
                    "Mover a Raíz y Eliminar",
                    "Cancelar");

                if (!confirmed) return;

                foreach (var s in secretsInFolder)
                {
                    var idx = _secrets.IndexOf(s);
                    _secrets[idx] = s with { Folder = null, ModifiedAt = DateTime.Now };
                }
                await SaveSecretsAsync();
            }

            _knownFolders.Remove(folder.FolderKey);
            SaveKnownFolders();

            if (string.Equals(_currentFolderKey, folder.FolderKey, StringComparison.OrdinalIgnoreCase))
            {
                _currentFolderKey = null;
                txtActiveFolderTitle.Text = "📁 Todos los secretos";
            }

            RefreshFolders();
            RefreshList();
            NotificationService.ShowSuccess("Carpeta Eliminada", $"La carpeta '{folder.Name}' ha sido eliminada.", 2);
        }
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        RefreshList();
    }

    private void RefreshList()
    {
        var query = txtSearch.Text?.Trim() ?? "";

        IEnumerable<SecretEntry> filtered = _secrets;

        if (_currentFolderKey == "")
        {
            filtered = filtered.Where(s => string.IsNullOrWhiteSpace(s.Folder));
        }
        else if (!string.IsNullOrEmpty(_currentFolderKey))
        {
            filtered = filtered.Where(s => string.Equals(s.Folder, _currentFolderKey, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrEmpty(query))
        {
            filtered = filtered.Where(s =>
                s.Key.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                (s.Folder != null && s.Folder.Contains(query, StringComparison.OrdinalIgnoreCase)) ||
                (s.Description != null && s.Description.Contains(query, StringComparison.OrdinalIgnoreCase)) ||
                s.Value.Contains(query, StringComparison.OrdinalIgnoreCase));
        }

        var list = filtered.Select(s => new SecretDisplayItem(s)).ToList();
        _displaySecrets = new ObservableCollection<SecretDisplayItem>(list);
        lstSecrets.ItemsSource = _displaySecrets;

        txtActiveFolderSubtitle.Text = $"({_displaySecrets.Count} secreto{(_displaySecrets.Count != 1 ? "s" : "")})";

        // Mostrar estado vacio si aplica
        if (_displaySecrets.Count == 0 && !string.IsNullOrEmpty(_currentFolderKey))
        {
            pnlEmptyFolderState.Visibility = Visibility.Visible;
            scrollSecretsList.Visibility = Visibility.Collapsed;
        }
        else
        {
            pnlEmptyFolderState.Visibility = Visibility.Collapsed;
            scrollSecretsList.Visibility = Visibility.Visible;
        }
    }

    private void ToggleRevealSecretButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: SecretDisplayItem item })
        {
            item.IsRevealed = !item.IsRevealed;
        }
    }

    private void AddFolderButton_Click(object sender, RoutedEventArgs e)
    {
        txtNewFolderName.Text = "";
        pnlFolderDialog.Visibility = Visibility.Visible;
        txtNewFolderName.Focus();
    }

    private void CancelFolderDialog_Click(object sender, RoutedEventArgs e)
    {
        pnlFolderDialog.Visibility = Visibility.Collapsed;
    }

    private void ConfirmAddFolder_Click(object sender, RoutedEventArgs e)
    {
        var folderName = txtNewFolderName.Text.Trim();
        if (string.IsNullOrWhiteSpace(folderName))
        {
            NotificationService.ShowWarning("Validación", "Ingresa un nombre para la carpeta.");
            return;
        }

        pnlFolderDialog.Visibility = Visibility.Collapsed;

        // Registrar la carpeta de forma independiente
        _knownFolders.Add(folderName);
        SaveKnownFolders();

        // Seleccionar la nueva carpeta inmediatamente
        _currentFolderKey = folderName;
        txtActiveFolderTitle.Text = $"🗂️ {folderName}";

        RefreshFolders();
        RefreshList();

        NotificationService.ShowSuccess("Carpeta Creada", $"Carpeta '{folderName}' creada. Puedes agregarle secretos cuando desees.", 3);
    }

    private void AddSecretToCurrentFolder_Click(object sender, RoutedEventArgs e)
    {
        AddSecretButton_Click(sender, e);
    }

    private void PopulateFolderComboBox(System.Windows.Controls.ComboBox comboBox, string? selectedFolder)
    {
        var existingFolders = _knownFolders
            .OrderBy(f => f)
            .ToList();

        comboBox.ItemsSource = existingFolders;
        comboBox.Text = selectedFolder ?? "";
    }

    private void AddSecretButton_Click(object sender, RoutedEventArgs e)
    {
        _editingSecret = null;
        txtDialogTitle.Text = "Agregar Nuevo Secreto";
        txtSecretKey.Text = "";
        txtSecretValue.Text = "";
        txtSecretDescription.Text = "";
        cmbSecretEnvironment.SelectedIndex = 0;
        txtDialogError.Visibility = Visibility.Collapsed;

        var defaultFolder = !string.IsNullOrEmpty(_currentFolderKey) ? _currentFolderKey : "";
        PopulateFolderComboBox(cmbSecretFolder, defaultFolder);

        pnlDialog.Visibility = Visibility.Visible;
        txtSecretKey.Focus();
    }

    private void EditSecretButton_Click(object sender, RoutedEventArgs e)
    {
        SecretEntry? target = null;

        if (sender is Button { Tag: SecretDisplayItem item })
            target = item.Secret;
        else if (sender is Button { Tag: SecretEntry entry })
            target = entry;

        if (target != null)
        {
            _editingSecret = target;
            txtDialogTitle.Text = "Editar Secreto";
            txtSecretKey.Text = target.Key;
            txtSecretValue.Text = target.Value;
            txtSecretDescription.Text = target.Description ?? "";
            txtDialogError.Visibility = Visibility.Collapsed;

            PopulateFolderComboBox(cmbSecretFolder, target.Folder);

            if (string.IsNullOrWhiteSpace(target.Environment))
            {
                cmbSecretEnvironment.SelectedIndex = 0;
            }
            else
            {
                var envItem = cmbSecretEnvironment.Items.Cast<ComboBoxItem>()
                    .FirstOrDefault(i => string.Equals(i.Content?.ToString(), target.Environment, StringComparison.OrdinalIgnoreCase));
                if (envItem != null)
                    cmbSecretEnvironment.SelectedItem = envItem;
                else
                    cmbSecretEnvironment.SelectedIndex = 0;
            }

            pnlDialog.Visibility = Visibility.Visible;
            txtSecretKey.Focus();
        }
    }

    #region Quick Move Secret Feature

    private void QuickMoveSecretButton_Click(object sender, RoutedEventArgs e)
    {
        SecretEntry? target = null;

        if (sender is Button { Tag: SecretDisplayItem item })
            target = item.Secret;
        else if (sender is Button { Tag: SecretEntry entry })
            target = entry;

        if (target != null)
        {
            _movingSecret = target;
            txtMoveSecretName.Text = $"Secreto: {target.Key} (Actual: {target.Folder ?? "Sin carpeta"})";
            PopulateFolderComboBox(cmbMoveTargetFolder, target.Folder);
            pnlMoveDialog.Visibility = Visibility.Visible;
        }
    }

    private void CancelMoveDialog_Click(object sender, RoutedEventArgs e)
    {
        pnlMoveDialog.Visibility = Visibility.Collapsed;
        _movingSecret = null;
    }

    private async void ConfirmMoveSecret_Click(object sender, RoutedEventArgs e)
    {
        if (_movingSecret == null) return;

        var targetFolder = cmbMoveTargetFolder.Text?.Trim();
        if (string.IsNullOrWhiteSpace(targetFolder)) targetFolder = null;

        if (string.Equals(_movingSecret.Folder, targetFolder, StringComparison.OrdinalIgnoreCase))
        {
            pnlMoveDialog.Visibility = Visibility.Collapsed;
            _movingSecret = null;
            return;
        }

        var oldFullKey = _movingSecret.FullKey;
        var oldFolder = _movingSecret.Folder;

        // Registrar nueva carpeta en known folders si no existia
        if (!string.IsNullOrWhiteSpace(targetFolder))
        {
            _knownFolders.Add(targetFolder);
            SaveKnownFolders();
        }

        // Actualizar secreto
        var index = _secrets.IndexOf(_movingSecret);
        var updated = _movingSecret with
        {
            Folder = targetFolder,
            ModifiedAt = DateTime.Now
        };
        _secrets[index] = updated;

        var newFullKey = updated.FullKey;

        // Mitigar y sincronizar API Keys afectadas
        var apiKeysModified = false;
        foreach (var key in _apiKeys)
        {
            var allowed = key.Permissions.AllowedSecrets;
            if (allowed != null && (allowed.Contains(oldFullKey, StringComparer.OrdinalIgnoreCase) || allowed.Contains(_movingSecret.Key, StringComparer.OrdinalIgnoreCase)))
            {
                allowed.RemoveAll(k => k.Equals(oldFullKey, StringComparison.OrdinalIgnoreCase) || k.Equals(_movingSecret.Key, StringComparison.OrdinalIgnoreCase));
                if (!allowed.Contains(newFullKey, StringComparer.OrdinalIgnoreCase))
                {
                    allowed.Add(newFullKey);
                }
                apiKeysModified = true;
            }
        }

        await SaveSecretsAsync();
        if (apiKeysModified)
        {
            await SaveApiKeysAsync();
        }

        pnlMoveDialog.Visibility = Visibility.Collapsed;
        _movingSecret = null;

        RefreshFolders();
        RefreshList();

        NotificationService.ShowSuccess("Secreto Movido",
            $"Secreto movido a '{targetFolder ?? "Sin carpeta"}'. Permisos de API Keys actualizados.", 4);
    }

    #endregion

    private async void DeleteSecretButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: Guid id })
        {
            var secret = _secrets.FirstOrDefault(s => s.Id == id);
            if (secret is null) return;

            var confirmed = await DialogService.ConfirmDangerousAsync(
                "Eliminar Secreto",
                $"¿Estás seguro de eliminar el secreto '{secret.Key}'?\n\nEsta acción no se puede deshacer.",
                "Eliminar",
                "Cancelar");

            if (confirmed)
            {
                _secrets.Remove(secret);
                await SaveSecretsAsync();
                UpdateSecretCount();
                RefreshFolders();
                RefreshList();

                NotificationService.ShowSuccess("Eliminado", $"Secreto '{secret.Key}' eliminado correctamente.", 3);
            }
        }
    }

    private void CopyValueButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string value })
        {
            Clipboard.SetText(value);
            NotificationService.ShowSuccess("Copiado", "Valor copiado al portapapeles.", 2);
        }
    }

    private void CancelDialogButton_Click(object sender, RoutedEventArgs e)
    {
        pnlDialog.Visibility = Visibility.Collapsed;
        _editingSecret = null;
    }

    private void QuickTemplate_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string template })
        {
            if (string.IsNullOrWhiteSpace(txtSecretKey.Text) || !txtSecretKey.Text.Contains(':'))
            {
                txtSecretKey.Text = template;
                txtSecretKey.CaretIndex = txtSecretKey.Text.Length;
            }
            else
            {
                var suffix = txtSecretKey.Text.Split(':').Last();
                txtSecretKey.Text = template + suffix;
                txtSecretKey.CaretIndex = txtSecretKey.Text.Length;
            }
            txtSecretKey.Focus();
        }
    }

    private void GeneratePassword_Click(object sender, RoutedEventArgs e)
    {
        const string validChars = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789!@#$%&*+=-";
        var bytes = RandomNumberGenerator.GetBytes(24);
        var sb = new StringBuilder(24);
        foreach (var b in bytes)
        {
            sb.Append(validChars[b % validChars.Length]);
        }
        txtSecretValue.Text = sb.ToString();
        NotificationService.ShowSuccess("Generado", "Contraseña segura generada.", 2);
    }

    private void GenerateAesKey_Click(object sender, RoutedEventArgs e)
    {
        var keyBytes = RandomNumberGenerator.GetBytes(32);
        txtSecretValue.Text = Convert.ToBase64String(keyBytes);
        NotificationService.ShowSuccess("Generado", "Llave AES-256 (Base64) generada.", 2);
    }

    private async void SaveSecretButton_Click(object sender, RoutedEventArgs e)
    {
        var key = txtSecretKey.Text.Trim();
        var value = txtSecretValue.Text;
        var folder = cmbSecretFolder.Text?.Trim();
        if (string.IsNullOrWhiteSpace(folder)) folder = null;

        var description = txtSecretDescription.Text.Trim();
        var selectedEnvItem = cmbSecretEnvironment.SelectedItem as ComboBoxItem;
        var environment = selectedEnvItem != null && selectedEnvItem.Content.ToString() != "(Sin entorno)"
            ? selectedEnvItem.Content.ToString()
            : null;

        if (string.IsNullOrWhiteSpace(key))
        {
            txtDialogError.Text = "La clave del secreto es requerida.";
            txtDialogError.Visibility = Visibility.Visible;
            return;
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            txtDialogError.Text = "El valor del secreto es requerido.";
            txtDialogError.Visibility = Visibility.Visible;
            return;
        }

        var existingKey = _secrets.FirstOrDefault(s =>
            s.Key.Equals(key, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(s.Folder, folder, StringComparison.OrdinalIgnoreCase) &&
            s.Id != _editingSecret?.Id);

        if (existingKey is not null)
        {
            txtDialogError.Text = "Ya existe un secreto con esta clave en esta carpeta/proyecto.";
            txtDialogError.Visibility = Visibility.Visible;
            return;
        }

        if (!string.IsNullOrWhiteSpace(folder))
        {
            _knownFolders.Add(folder);
            SaveKnownFolders();
        }

        if (_editingSecret is not null)
        {
            var oldFullKey = _editingSecret.FullKey;
            var index = _secrets.IndexOf(_secrets.First(s => s.Id == _editingSecret.Id));
            var updated = _editingSecret with
            {
                Key = key,
                Value = value,
                Folder = folder,
                Environment = environment,
                Description = string.IsNullOrWhiteSpace(description) ? null : description,
                ModifiedAt = DateTime.Now
            };
            _secrets[index] = updated;

            // Si cambio de carpeta, sincronizar API Keys
            if (!string.Equals(oldFullKey, updated.FullKey, StringComparison.OrdinalIgnoreCase))
            {
                var apiKeysModified = false;
                foreach (var k in _apiKeys)
                {
                    if (k.Permissions.AllowedSecrets.Contains(oldFullKey, StringComparer.OrdinalIgnoreCase))
                    {
                        k.Permissions.AllowedSecrets.RemoveAll(x => x.Equals(oldFullKey, StringComparison.OrdinalIgnoreCase));
                        k.Permissions.AllowedSecrets.Add(updated.FullKey);
                        apiKeysModified = true;
                    }
                }
                if (apiKeysModified) await SaveApiKeysAsync();
            }
        }
        else
        {
            var newSecret = new SecretEntry(
                Guid.NewGuid(),
                key,
                value,
                folder,
                string.IsNullOrWhiteSpace(description) ? null : description,
                environment,
                null,
                DateTime.Now,
                null);

            _secrets.Add(newSecret);
        }

        await SaveSecretsAsync();
        UpdateSecretCount();
        RefreshFolders();
        RefreshList();

        pnlDialog.Visibility = Visibility.Collapsed;
        _editingSecret = null;
    }

    #endregion

    #region API Keys Management

    private void ApiKeysButton_Click(object sender, RoutedEventArgs e)
    {
        BuildApiKeyTree();
        lstApiKeys.ItemsSource = _apiKeys;
        pnlGeneratedKey.Visibility = Visibility.Collapsed;
        txtNewKeyName.Text = "";
        txtSecretFilter.Text = "";
        rbFullAccess.IsChecked = true;
        chkCanList.IsChecked = true;
        pnlSecretsSelection.Visibility = Visibility.Collapsed;
        pnlApiKeys.Visibility = Visibility.Visible;
    }

    private void BuildApiKeyTree()
    {
        _apiKeyTreeNodes = new ObservableCollection<ApiKeyTreeNode>();

        // 1. Folders
        var folderNames = _knownFolders.OrderBy(f => f).ToList();
        foreach (var folder in folderNames)
        {
            var secretsInFolder = _secrets
                .Where(s => string.Equals(s.Folder, folder, StringComparison.OrdinalIgnoreCase))
                .OrderBy(s => s.Key)
                .ToList();

            var folderNode = new ApiKeyTreeNode
            {
                Name = folder,
                DisplayText = $"{folder} ({secretsInFolder.Count} secreto{(secretsInFolder.Count == 1 ? "" : "s")})",
                Icon = "🗂️",
                IsFolder = true,
                FolderName = folder,
                IsSelected = false,
                IsExpanded = true
            };

            foreach (var s in secretsInFolder)
            {
                var secretNode = new ApiKeyTreeNode
                {
                    Name = s.Key,
                    DisplayText = s.Key + (string.IsNullOrEmpty(s.Environment) ? "" : $" [{s.Environment}]"),
                    FullKey = s.FullKey,
                    Icon = "🔑",
                    IsFolder = false,
                    FolderName = folder,
                    Parent = folderNode,
                    IsSelected = false
                };
                folderNode.Children.Add(secretNode);
            }

            _apiKeyTreeNodes.Add(folderNode);
        }

        // 2. Root secrets (without folder)
        var rootSecrets = _secrets
            .Where(s => string.IsNullOrEmpty(s.Folder))
            .OrderBy(s => s.Key)
            .ToList();

        if (rootSecrets.Count > 0)
        {
            var rootFolderNode = new ApiKeyTreeNode
            {
                Name = "(Sin Carpeta)",
                DisplayText = $"Sin Carpeta ({rootSecrets.Count} secreto{(rootSecrets.Count == 1 ? "" : "s")})",
                Icon = "📂",
                IsFolder = true,
                FolderName = null,
                IsSelected = false,
                IsExpanded = true
            };

            foreach (var s in rootSecrets)
            {
                var secretNode = new ApiKeyTreeNode
                {
                    Name = s.Key,
                    DisplayText = s.Key + (string.IsNullOrEmpty(s.Environment) ? "" : $" [{s.Environment}]"),
                    FullKey = s.FullKey,
                    Icon = "🔑",
                    IsFolder = false,
                    FolderName = null,
                    Parent = rootFolderNode,
                    IsSelected = false
                };
                rootFolderNode.Children.Add(secretNode);
            }

            _apiKeyTreeNodes.Add(rootFolderNode);
        }

        tvApiKeyPermissions.ItemsSource = _apiKeyTreeNodes;
    }

    private void AccessLevel_Changed(object sender, RoutedEventArgs e)
    {
        if (pnlSecretsSelection == null) return;

        pnlSecretsSelection.Visibility = rbRestrictedAccess.IsChecked == true
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void SecretTreeFilter_TextChanged(object sender, TextChangedEventArgs e)
    {
        var filter = txtSecretFilter.Text.Trim();
        foreach (var node in _apiKeyTreeNodes)
        {
            node.ApplyFilter(filter);
        }
    }

    private void SelectAllTreeSecrets_Click(object sender, RoutedEventArgs e)
    {
        foreach (var node in _apiKeyTreeNodes)
        {
            node.SetIsSelected(true, updateChildren: true, updateParent: false);
        }
    }

    private void SelectNoTreeSecrets_Click(object sender, RoutedEventArgs e)
    {
        foreach (var node in _apiKeyTreeNodes)
        {
            node.SetIsSelected(false, updateChildren: true, updateParent: false);
        }
    }

    private void ToggleExpandTree_Click(object sender, RoutedEventArgs e)
    {
        _isTreeExpanded = !_isTreeExpanded;
        SetTreeExpanded(_apiKeyTreeNodes, _isTreeExpanded);
    }

    private void SetTreeExpanded(IEnumerable<ApiKeyTreeNode> nodes, bool expanded)
    {
        foreach (var node in nodes)
        {
            node.IsExpanded = expanded;
            if (node.Children.Count > 0)
            {
                SetTreeExpanded(node.Children, expanded);
            }
        }
    }

    private void ViewApiKeyPermissions_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: Guid id })
        {
            var apiKey = _apiKeys.FirstOrDefault(k => k.Id == id);
            if (apiKey == null) return;

            var permissions = apiKey.Permissions;
            string message;

            if (permissions.Level == AccessLevel.Full)
            {
                message = "Nivel: Acceso Total\n\nPuede acceder a TODOS los secretos y proyectos del baúl.";
                NotificationService.ShowInfo($"API Key: {apiKey.Name}", message, 8);
            }
            else
            {
                var prefixes = permissions.AllowedPrefixes.Count > 0
                    ? string.Join(", ", permissions.AllowedPrefixes)
                    : "(ninguno)";

                var secretsCount = permissions.AllowedSecrets.Count;
                var secretsList = secretsCount > 0
                    ? string.Join(", ", permissions.AllowedSecrets.Take(6)) + (secretsCount > 6 ? $" (+{secretsCount - 6} más)" : "")
                    : "(ninguno)";

                message = $"Nivel: Restringido\nCarpetas/Proyectos: {prefixes}\nSecretos Individuales ({secretsCount}): {secretsList}\nListar: {(permissions.CanList ? "Sí" : "No")}";
                NotificationService.ShowInfo($"API Key: {apiKey.Name}", message, 10);
            }
        }
    }

    private async void GenerateApiKeyButton_Click(object sender, RoutedEventArgs e)
    {
        var name = txtNewKeyName.Text.Trim();

        if (string.IsNullOrWhiteSpace(name))
        {
            NotificationService.ShowWarning("Validación", "Ingresa un nombre para la API Key.");
            return;
        }

        AccessLevel accessLevel;
        var allowedSecrets = new List<string>();
        var allowedPrefixes = new List<string>();
        var canList = chkCanList.IsChecked == true;

        if (rbFullAccess.IsChecked == true)
        {
            accessLevel = AccessLevel.Full;
            canList = true;
        }
        else
        {
            accessLevel = AccessLevel.Restricted;

            foreach (var rootNode in _apiKeyTreeNodes)
            {
                rootNode.CollectSelectedKeys(allowedSecrets, allowedPrefixes);
            }

            if (allowedPrefixes.Count == 0 && allowedSecrets.Count == 0)
            {
                NotificationService.ShowWarning("Validación", "Selecciona al menos una Carpeta/Proyecto o un Secreto en el árbol.");
                return;
            }
        }

        var (rawApiKey, keyHash) = ApiKeyService.GenerateApiKey();
        var permissions = new ApiKeyPermissions(
            accessLevel, 
            allowedSecrets.Distinct(StringComparer.OrdinalIgnoreCase).ToList(), 
            allowedPrefixes.Distinct(StringComparer.OrdinalIgnoreCase).ToList(), 
            canList);

        var newApiKey = new ApiKeyEntry(
            Guid.NewGuid(),
            name,
            keyHash,
            null,
            DateTime.Now,
            null,
            true,
            permissions);

        _apiKeys.Add(newApiKey);
        await SaveApiKeysAsync();

        txtGeneratedKey.Text = rawApiKey;
        pnlGeneratedKey.Visibility = Visibility.Visible;

        txtNewKeyName.Text = "";
        rbFullAccess.IsChecked = true;

        NotificationService.ShowSuccess("API Key Generada", $"API Key '{name}' creada con éxito.", 4);
    }

    private async void DeleteApiKeyButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: Guid id })
        {
            var apiKey = _apiKeys.FirstOrDefault(k => k.Id == id);
            if (apiKey == null) return;

            var confirmed = await DialogService.ConfirmDangerousAsync(
                "Revocar API Key",
                $"¿Revocar la API Key '{apiKey.Name}'?\n\nLas aplicaciones que usen esta clave perderán acceso inmediatamente.",
                "Revocar",
                "Cancelar");

            if (confirmed)
            {
                _apiKeys.Remove(apiKey);
                await SaveApiKeysAsync();

                NotificationService.ShowSuccess("Revocada", $"API Key '{apiKey.Name}' ha sido revocada.", 3);
            }
        }
    }

    private void CopyApiKeyButton_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(txtGeneratedKey.Text))
        {
            Clipboard.SetText(txtGeneratedKey.Text);
            NotificationService.ShowSuccess("Copiado", "API Key copiada al portapapeles.", 2);
        }
    }

    private void CloseApiKeysButton_Click(object sender, RoutedEventArgs e)
    {
        pnlApiKeys.Visibility = Visibility.Collapsed;
        txtGeneratedKey.Text = "";
        pnlGeneratedKey.Visibility = Visibility.Collapsed;
    }

    #endregion

    #region Audit Log

    private void AuditLogButton_Click(object sender, RoutedEventArgs e)
    {
        RefreshAuditLogs();
        pnlAuditLog.Visibility = Visibility.Visible;
    }

    private void RefreshAuditLogs()
    {
        var stats = _secretServer.AuditService.GetStatistics();
        txtTotalRequests.Text = stats.TotalRequests.ToString("N0");
        txtSuccessRequests.Text = stats.SuccessfulRequests.ToString("N0");
        txtFailedRequests.Text = stats.FailedRequests.ToString("N0");
        txtUniqueClients.Text = stats.UniqueApiKeys.ToString("N0");

        var logs = _secretServer.AuditService.GetRecentLogs(200);
        lstAuditLogs.ItemsSource = logs;
    }

    private void RefreshAuditLogButton_Click(object sender, RoutedEventArgs e)
    {
        RefreshAuditLogs();
        NotificationService.ShowSuccess("Actualizado", "Registro de auditoría actualizado.", 1);
    }

    private async void ExportAuditLogsButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var logs = _secretServer.AuditService.GetRecentLogs(2000);
            if (logs.Count == 0)
            {
                NotificationService.ShowWarning("Auditoría", "No hay registros de auditoría para exportar.");
                return;
            }

            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Exportar Registro de Auditoría",
                Filter = "Archivo CSV (*.csv)|*.csv",
                FileName = $"Arca_Auditoria_{DateTime.Now:yyyyMMdd_HHmmss}.csv"
            };

            if (dialog.ShowDialog() == true)
            {
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("Timestamp,ApiKeyName,ApiKeyId,Action,SecretKey,Success,ErrorMessage");
                foreach (var log in logs)
                {
                    var time = log.Timestamp.ToString("yyyy-MM-dd HH:mm:ss");
                    var name = $"\"{(log.ApiKeyName ?? "").Replace("\"", "\"\"")}\"";
                    var id = log.ApiKeyId;
                    var action = log.Action;
                    var secret = string.IsNullOrEmpty(log.SecretKey) ? "" : $"\"{log.SecretKey.Replace("\"", "\"\"")}\"";
                    var success = log.Success ? "OK" : "FAILED";
                    var error = string.IsNullOrEmpty(log.ErrorMessage) ? "" : $"\"{log.ErrorMessage.Replace("\"", "\"\"")}\"";

                    sb.AppendLine($"{time},{name},{id},{action},{secret},{success},{error}");
                }

                await System.IO.File.WriteAllTextAsync(dialog.FileName, sb.ToString(), System.Text.Encoding.UTF8);
                NotificationService.ShowSuccess("Exportación Exitosa", $"Se exportaron {logs.Count} registros a CSV.", 3);
            }
        }
        catch (Exception ex)
        {
            NotificationService.ShowError("Error al Exportar", ex.Message);
        }
    }

    private void CloseAuditLogButton_Click(object sender, RoutedEventArgs e)
    {
        pnlAuditLog.Visibility = Visibility.Collapsed;
    }

    #endregion

    #region Backup (Export/Import)

    private void BackupButton_Click(object sender, RoutedEventArgs e)
    {
        txtExportPassword.Password = "";
        txtExportPasswordConfirm.Password = "";
        txtImportPassword.Password = "";

        var count = _autoBackupService.GetSnapshotCount();
        var lastTime = _autoBackupService.LastBackupTime.HasValue 
            ? _autoBackupService.LastBackupTime.Value.ToString("HH:mm:ss")
            : "Al iniciar";

        txtAutoBackupStatus.Text = $"Instantáneas rotativas: {count} respaldos guardados (Último: {lastTime}).";
        pnlBackup.Visibility = Visibility.Visible;
    }

    private void CreateInstantAutoBackup_Click(object sender, RoutedEventArgs e)
    {
        var ok = _autoBackupService.CreateSnapshot();
        if (ok)
        {
            var count = _autoBackupService.GetSnapshotCount();
            txtAutoBackupStatus.Text = $"Instantáneas rotativas: {count} respaldos guardados (Último: {DateTime.Now:HH:mm:ss}).";
            NotificationService.ShowSuccess("Respaldo Creado", "Instantánea del baúl generada con éxito.", 2);
        }
        else
        {
            NotificationService.ShowWarning("Respaldo", "No se pudo crear la instantánea.");
        }
    }

    private void OpenBackupsFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (System.IO.Directory.Exists(_autoBackupService.BackupDirectory))
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = _autoBackupService.BackupDirectory,
                    UseShellExecute = true,
                    Verb = "open"
                });
            }
            else
            {
                NotificationService.ShowWarning("Carpeta no encontrada", "Aún no se han generado respaldos.");
            }
        }
        catch (Exception ex)
        {
            NotificationService.ShowError("Error al abrir carpeta", ex.Message);
        }
    }

    private void CloseBackupButton_Click(object sender, RoutedEventArgs e)
    {
        pnlBackup.Visibility = Visibility.Collapsed;
    }

    private async void ExportVaultButton_Click(object sender, RoutedEventArgs e)
    {
        var password = txtExportPassword.Password;
        var confirm = txtExportPasswordConfirm.Password;

        if (string.IsNullOrWhiteSpace(password))
        {
            NotificationService.ShowWarning("Validación", "Ingresa una contraseña para el respaldo.");
            return;
        }

        if (password != confirm)
        {
            NotificationService.ShowWarning("Validación", "Las contraseñas no coinciden.");
            return;
        }

        if (password.Length < 8)
        {
            NotificationService.ShowWarning("Validación", "La contraseña debe tener al menos 8 caracteres.");
            return;
        }

        var saveDialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Exportar Baúl",
            Filter = "Arca Vault Backup (*.arcavault)|*.arcavault",
            DefaultExt = ".arcavault",
            InitialDirectory = !string.IsNullOrEmpty(_settingsService.Current.DefaultExportDirectory) && Directory.Exists(_settingsService.Current.DefaultExportDirectory) 
                ? _settingsService.Current.DefaultExportDirectory 
                : null,
            FileName = $"arca-backup-{DateTime.Now:yyyy-MM-dd}"
        };

        if (saveDialog.ShowDialog() != true)
            return;

        try
        {
            await _exportService.ExportAsync(_secrets, _apiKeys, password, saveDialog.FileName);

            NotificationService.ShowSuccess("Exportación Completa",
                $"Baúl exportado con éxito.\n{_secrets.Count} secretos, {_apiKeys.Count} API Keys.", 5);

            pnlBackup.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            NotificationService.ShowError("Error de Exportación", $"Error al exportar baúl: {ex.Message}");
        }
    }

    private async void ImportVaultButton_Click(object sender, RoutedEventArgs e)
    {
        var password = txtImportPassword.Password;

        if (string.IsNullOrWhiteSpace(password))
        {
            NotificationService.ShowWarning("Validación", "Ingresa la contraseña del archivo de respaldo.");
            return;
        }

        var openDialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Importar Baúl",
            Filter = "Arca Vault Backup (*.arcavault)|*.arcavault",
            DefaultExt = ".arcavault"
        };

        if (openDialog.ShowDialog() != true)
            return;

        try
        {
            if (!await _exportService.IsValidExportFileAsync(openDialog.FileName))
            {
                NotificationService.ShowError("Archivo Inválido", "El archivo no es un respaldo válido de Arca.");
                return;
            }

            var exportData = await _exportService.LoadExportFileAsync(openDialog.FileName, password);
            if (exportData == null)
            {
                NotificationService.ShowError("Error de Importación", "No se pudo leer el archivo de respaldo.");
                return;
            }

            var overwrite = chkOverwriteExisting.IsChecked == true;
            var importApiKeys = chkImportApiKeys.IsChecked == true;

            int secretsImported = 0, secretsSkipped = 0;
            int apiKeysImported = 0, apiKeysSkipped = 0;

            foreach (var secret in exportData.Secrets)
            {
                if (!string.IsNullOrWhiteSpace(secret.Folder))
                {
                    _knownFolders.Add(secret.Folder.Trim());
                }

                var existing = _secrets.FirstOrDefault(s =>
                    s.Key.Equals(secret.Key, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(s.Folder, secret.Folder, StringComparison.OrdinalIgnoreCase));

                if (existing != null)
                {
                    if (overwrite)
                    {
                        var index = _secrets.IndexOf(existing);
                        _secrets[index] = existing with
                        {
                            Value = secret.Value,
                            Folder = secret.Folder,
                            Environment = secret.Environment,
                            Description = secret.Description,
                            ModifiedAt = DateTime.Now
                        };
                        secretsImported++;
                    }
                    else
                    {
                        secretsSkipped++;
                    }
                }
                else
                {
                    _secrets.Add(new SecretEntry(
                        Guid.NewGuid(),
                        secret.Key,
                        secret.Value,
                        secret.Folder,
                        secret.Description,
                        secret.Environment,
                        secret.Tags,
                        DateTime.Now,
                        null));
                    secretsImported++;
                }
            }

            SaveKnownFolders();

            if (importApiKeys && exportData.ApiKeys != null)
            {
                foreach (var key in exportData.ApiKeys)
                {
                    var existing = _apiKeys.FirstOrDefault(k => k.Name.Equals(key.Name, StringComparison.OrdinalIgnoreCase));

                    if (existing == null)
                    {
                        if (Enum.TryParse<AccessLevel>(key.AccessLevel, out var level))
                        {
                            var permissions = new ApiKeyPermissions(level, key.AllowedSecrets ?? [], key.AllowedPrefixes ?? [], key.CanList);
                            var importedKey = new ApiKeyEntry(
                                Guid.NewGuid(),
                                key.Name,
                                ApiKeyService.ComputeHash(Guid.NewGuid().ToString("N")),
                                key.Description,
                                key.CreatedAt,
                                null,
                                false,
                                permissions);

                            _apiKeys.Add(importedKey);
                            apiKeysImported++;
                        }
                    }
                    else
                    {
                        apiKeysSkipped++;
                    }
                }
            }

            await SaveSecretsAsync();
            if (importApiKeys)
            {
                await SaveApiKeysAsync();
            }

            UpdateSecretCount();
            RefreshFolders();
            RefreshList();

            var message = $"Importación finalizada:\n- Secretos: {secretsImported} importados, {secretsSkipped} omitidos.";
            if (importApiKeys)
            {
                message += $"\n- API Keys: {apiKeysImported} importadas (inactivas).";
            }

            NotificationService.ShowSuccess("Importación Completa", message, 6);
            pnlBackup.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            NotificationService.ShowError("Error de Importación", $"Error al importar baúl: {ex.Message}");
        }
    }

    #endregion

    #region Settings Management

    private void ApplyInitialSettings()
    {
        var s = _settingsService.Current;
        _autoBackupService.Configure(
            s.CustomBackupDirectory,
            s.AutoBackupIntervalHours,
            s.MaxAutoBackupSnapshots,
            s.AutoBackupEnabled);
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        var s = _settingsService.Current;

        chkAutoBackupEnabled.IsChecked = s.AutoBackupEnabled;

        // Select interval item
        foreach (System.Windows.Controls.ComboBoxItem item in cmbAutoBackupInterval.Items)
        {
            if (int.TryParse(item.Tag?.ToString(), out var hours) && hours == s.AutoBackupIntervalHours)
            {
                cmbAutoBackupInterval.SelectedItem = item;
                break;
            }
        }

        // Select max snapshots item
        foreach (System.Windows.Controls.ComboBoxItem item in cmbMaxSnapshots.Items)
        {
            if (int.TryParse(item.Tag?.ToString(), out var count) && count == s.MaxAutoBackupSnapshots)
            {
                cmbMaxSnapshots.SelectedItem = item;
                break;
            }
        }

                // Select language item
        foreach (System.Windows.Controls.ComboBoxItem item in cmbLanguage.Items)
        {
            if (string.Equals(item.Tag?.ToString(), s.Language, StringComparison.OrdinalIgnoreCase))
            {
                cmbLanguage.SelectedItem = item;
                break;
            }
        }

        txtCustomBackupDir.Text = s.CustomBackupDirectory ?? "";
        txtDefaultExportDir.Text = s.DefaultExportDirectory ?? "";
        chkMinimizeToTray.IsChecked = s.MinimizeToTrayOnClose;
        chkAuditLogging.IsChecked = s.AuditLoggingEnabled;

        pnlSettings.Visibility = Visibility.Visible;
    }

    private void BrowseBackupDir_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Selecciona la carpeta para almacenar los respaldos automáticos",
            UseDescriptionForTitle = true
        };

        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            txtCustomBackupDir.Text = dialog.SelectedPath;
        }
    }

    private void BrowseExportDir_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Selecciona la carpeta predeterminada para exportaciones de baúl",
            UseDescriptionForTitle = true
        };

        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            txtDefaultExportDir.Text = dialog.SelectedPath;
        }
    }

    private void CloseSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        pnlSettings.Visibility = Visibility.Collapsed;
    }

    private void SaveSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        int intervalHours = 6;
        if (cmbAutoBackupInterval.SelectedItem is System.Windows.Controls.ComboBoxItem intervalItem &&
            int.TryParse(intervalItem.Tag?.ToString(), out var parsedHours))
        {
            intervalHours = parsedHours;
        }

        int maxSnapshots = 15;
        if (cmbMaxSnapshots.SelectedItem is System.Windows.Controls.ComboBoxItem snapshotItem &&
            int.TryParse(snapshotItem.Tag?.ToString(), out var parsedCount))
        {
            maxSnapshots = parsedCount;
        }

                var selectedLang = "es";
        if (cmbLanguage.SelectedItem is System.Windows.Controls.ComboBoxItem langItem &&
            langItem.Tag is string tag && !string.IsNullOrEmpty(tag))
        {
            selectedLang = tag;
        }

        var customBackup = txtCustomBackupDir.Text.Trim();
        var defaultExport = txtDefaultExportDir.Text.Trim();

        var updated = new AppSettings
        {
            AutoBackupEnabled = chkAutoBackupEnabled.IsChecked == true,
            AutoBackupIntervalHours = intervalHours,
            MaxAutoBackupSnapshots = maxSnapshots,
            CustomBackupDirectory = string.IsNullOrWhiteSpace(customBackup) ? null : customBackup,
            DefaultExportDirectory = string.IsNullOrWhiteSpace(defaultExport) ? null : defaultExport,
            MinimizeToTrayOnClose = chkMinimizeToTray.IsChecked == true,
                        Language = selectedLang,
            AuditLoggingEnabled = chkAuditLogging.IsChecked == true
        };

                _settingsService.Save(updated);
        LocalizationService.SetLanguage(updated.Language);

        _autoBackupService.Configure(
            updated.CustomBackupDirectory,
            updated.AutoBackupIntervalHours,
            updated.MaxAutoBackupSnapshots,
            updated.AutoBackupEnabled);

        pnlSettings.Visibility = Visibility.Collapsed;
        NotificationService.ShowSuccess("Ajustes Guardados", "Preferencias actualizadas correctamente.", 3);
    }

    #endregion

    #region About

    private void AboutButton_Click(object sender, RoutedEventArgs e)
    {
        CloseAllOpenModals();
        pnlAbout.Visibility = Visibility.Visible;
    }

    private void CloseAboutButton_Click(object sender, RoutedEventArgs e)
    {
        pnlAbout.Visibility = Visibility.Collapsed;
    }

    private void OpenGitHub_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "https://github.com/martir5913/Arca.NET",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            NotificationService.ShowError("Error", $"No se pudo abrir el navegador: {ex.Message}");
        }
    }

    private void CopyGitHub_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            System.Windows.Clipboard.SetText("https://github.com/martir5913/Arca.NET");
            NotificationService.ShowSuccess("Copiado", "Enlace de GitHub copiado al portapapeles.", 3);
        }
        catch (Exception ex)
        {
            NotificationService.ShowError("Error", $"Error al copiar: {ex.Message}");
        }
    }

    private void SendEmail_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "mailto:martir.dev@gmail.com?subject=Consulta%20Arca.NET",
                UseShellExecute = true
            });
        }
        catch
        {
            System.Windows.Clipboard.SetText("martir.dev@gmail.com");
            NotificationService.ShowSuccess("Copiado", "Correo martir.dev@gmail.com copiado al portapapeles.", 3);
        }
    }

    #endregion

    protected override void OnKeyDown(System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Escape)
        {
            CloseAllOpenModals();
            e.Handled = true;
        }
        base.OnKeyDown(e);
    }

    private void CloseAllOpenModals()
    {
        if (pnlAbout.Visibility == Visibility.Visible) pnlAbout.Visibility = Visibility.Collapsed;
        if (pnlSettings.Visibility == Visibility.Visible) pnlSettings.Visibility = Visibility.Collapsed;
        if (pnlApiKeys.Visibility == Visibility.Visible) pnlApiKeys.Visibility = Visibility.Collapsed;
        if (pnlAuditLog.Visibility == Visibility.Visible) pnlAuditLog.Visibility = Visibility.Collapsed;
        if (pnlBackup.Visibility == Visibility.Visible) pnlBackup.Visibility = Visibility.Collapsed;
        if (pnlDialog.Visibility == Visibility.Visible) pnlDialog.Visibility = Visibility.Collapsed;
        if (pnlMoveDialog.Visibility == Visibility.Visible) pnlMoveDialog.Visibility = Visibility.Collapsed;
        if (pnlFolderDialog.Visibility == Visibility.Visible) pnlFolderDialog.Visibility = Visibility.Collapsed;
    }

    private void LockButton_Click(object sender, RoutedEventArgs e)
    {
        _secretServer.Stop();
        Array.Clear(_derivedKey, 0, _derivedKey.Length);

        if (Application.Current is App app)
        {
            app.ShowLoginWindow();
        }
    }
}