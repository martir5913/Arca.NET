using Arca.Core.Entities;
using Arca.Core.Interfaces;
using Arca.Core.Security;
using Arca.Core.Services;
using Arca.NET.Controls;
using Arca.NET.Models;
using Arca.NET.Services;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
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

    private ObservableCollection<SecretEntry> _secrets = [];
    private ObservableCollection<ApiKeyEntry> _apiKeys = [];
    private SecretEntry? _editingSecret;

    public MainWindow(
        byte[] derivedKey,
        IVaultRepository vaultRepository,
        IAesGcmService aesGcmService,
        IKeyDerivationService keyDerivationService)
    {
        InitializeComponent();

        _derivedKey = derivedKey;
        _vaultRepository = vaultRepository;
        _aesGcmService = aesGcmService;
        _keyDerivationService = keyDerivationService;
        _secretServer = new EmbeddedSecretServer();

        // Configurar evento de uso de API Key
        _secretServer.ApiKeyUsed += OnApiKeyUsed;

        Loaded += MainWindow_Loaded;
        StateChanged += MainWindow_StateChanged;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        // Inicializar servicios de notificaciones y diálogos
        NotificationService.Initialize(notificationsContainer);
        DialogService.Initialize(dialogContainer);

        await LoadSecretsAsync();
        await LoadApiKeysAsync();

        // Iniciar servidor embebido para que otras apps puedan obtener secretos
        _secretServer.UpdateSecrets(_secrets);
        _secretServer.UpdateApiKeys(_apiKeys);
        _secretServer.RequireAuthentication = _apiKeys.Count > 0;
        _secretServer.Start();

        UpdateStatusBar();
    }

    private void MainWindow_StateChanged(object? sender, EventArgs e)
    {
        // Si se minimiza, enviar a la bandeja del sistema
        if (WindowState == WindowState.Minimized)
        {
            if (Application.Current is App app)
            {
                app.MinimizeToTray();
            }
        }
    }

    /// <summary>
    /// Llamado cuando la app se cierra completamente (no solo se oculta).
    /// </summary>
    public void OnAppShutdown()
    {
        _secretServer.Stop();
        _secretServer.Dispose();

        // Clear sensitive data
        Array.Clear(_derivedKey, 0, _derivedKey.Length);
    }

    private void UpdateStatusBar()
    {
        var authStatus = _secretServer.RequireAuthentication ? "🔐" : "⚠️";
        txtStatus.Text = $"Vault unlocked - SDK server active {authStatus}";

        if (!_secretServer.RequireAuthentication && _apiKeys.Count == 0)
        {
            txtStatus.Text += " (No API Keys - Open access!)";
        }
    }

    private async void OnApiKeyUsed(object? sender, string keyHash)
    {
        // Ejecutar en el hilo de la UI
        await Dispatcher.InvokeAsync(async () =>
        {
            // Actualizar LastUsedAt de la API Key
            var apiKey = _apiKeys.FirstOrDefault(k => k.KeyHash == keyHash);
            if (apiKey != null)
            {
                var index = _apiKeys.IndexOf(apiKey);
                _apiKeys[index] = apiKey with { LastUsedAt = DateTime.UtcNow };
                await SaveApiKeysAsync();
            }
        });
    }

    private async Task LoadSecretsAsync()
    {
        try
        {
            var secrets = await _vaultRepository.LoadSecretsAsync(_derivedKey);
            _secrets = new ObservableCollection<SecretEntry>(secrets);
            lstSecrets.ItemsSource = _secrets;

            // Actualizar servidor embebido
            _secretServer.UpdateSecrets(_secrets);

            UpdateSecretCount();

            // Actualizar App con el conteo
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

            // Actualizar servidor embebido
            _secretServer.UpdateApiKeys(_apiKeys);

            // Actualizar estado de la app
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

            // Actualizar servidor embebido
            _secretServer.UpdateApiKeys(_apiKeys);

            // Actualizar estado de la app
            UpdateStatusBar();
        }
        catch (Exception ex)
        {
            NotificationService.ShowError("Error", $"Error saving API keys: {ex.Message}");
        }
    }

    private void UpdateSecretCount()
    {
        txtSecretCount.Text = $"{_secrets.Count} secret{(_secrets.Count != 1 ? "s" : "")}";
    }

    private void LockButton_Click(object sender, RoutedEventArgs e)
    {
        // Detener servidor embebido
        _secretServer.Stop();

        // Clear sensitive data
        Array.Clear(_derivedKey, 0, _derivedKey.Length);

        // Volver a la pantalla de login via App
        if (Application.Current is App app)
        {
            app.ShowLoginWindow();
        }
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        // Minimizar a la bandeja del sistema
        if (Application.Current is App app)
        {
            app.MinimizeToTray();
        }
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var searchText = txtSearch.Text;

        if (string.IsNullOrWhiteSpace(searchText))
        {
            lstSecrets.ItemsSource = _secrets;
        }
        else
        {
            var filtered = _secrets.Where(s =>
                s.Key.Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
                (s.Description?.Contains(searchText, StringComparison.OrdinalIgnoreCase) ?? false))
                .ToList();

            lstSecrets.ItemsSource = filtered;
        }
    }

    private void AddSecretButton_Click(object sender, RoutedEventArgs e)
    {
        _editingSecret = null;
        txtDialogTitle.Text = "Add New Secret";
        txtSecretKey.Text = "";
        txtSecretValue.Text = "";
        txtSecretDescription.Text = "";
        txtDialogError.Visibility = Visibility.Collapsed;
        pnlDialog.Visibility = Visibility.Visible;
        txtSecretKey.Focus();
    }

    private void EditSecretButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: SecretEntry secret })
        {
            _editingSecret = secret;
            txtDialogTitle.Text = "Edit Secret";
            txtSecretKey.Text = secret.Key;
            txtSecretValue.Text = secret.Value;
            txtSecretDescription.Text = secret.Description ?? "";
            txtDialogError.Visibility = Visibility.Collapsed;
            pnlDialog.Visibility = Visibility.Visible;
            txtSecretKey.Focus();
        }
    }

    private async void DeleteSecretButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: Guid id })
        {
            var secret = _secrets.FirstOrDefault(s => s.Id == id);
            if (secret is null) return;

            var confirmed = await DialogService.ConfirmDangerousAsync(
                "Delete Secret",
                $"Are you sure you want to delete the secret '{secret.Key}'?\n\nThis action cannot be undone.",
                "Delete",
                "Cancel");

            if (confirmed)
            {
                _secrets.Remove(secret);
                await SaveSecretsAsync();
                UpdateSecretCount();
                RefreshList();
                
                NotificationService.ShowSuccess("Deleted", $"Secret '{secret.Key}' has been deleted.", 3);
            }
        }
    }

    private void CopyValueButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string value })
        {
            Clipboard.SetText(value);
            NotificationService.ShowSuccess("Copied", "Secret value copied to clipboard.", 2);
        }
    }

    private void CancelDialogButton_Click(object sender, RoutedEventArgs e)
    {
        pnlDialog.Visibility = Visibility.Collapsed;
        _editingSecret = null;
    }

    private async void SaveSecretButton_Click(object sender, RoutedEventArgs e)
    {
        var key = txtSecretKey.Text.Trim();
        var value = txtSecretValue.Text;
        var description = txtSecretDescription.Text.Trim();

        // Validation
        if (string.IsNullOrWhiteSpace(key))
        {
            txtDialogError.Text = "Key is required.";
            txtDialogError.Visibility = Visibility.Visible;
            return;
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            txtDialogError.Text = "Value is required.";
            txtDialogError.Visibility = Visibility.Visible;
            return;
        }

        // Check for duplicate key (excluding current editing secret)
        var existingKey = _secrets.FirstOrDefault(s =>
            s.Key.Equals(key, StringComparison.OrdinalIgnoreCase) &&
            s.Id != _editingSecret?.Id);

        if (existingKey is not null)
        {
            txtDialogError.Text = "A secret with this key already exists.";
            txtDialogError.Visibility = Visibility.Visible;
            return;
        }

        if (_editingSecret is not null)
        {
            // Update existing
            var index = _secrets.IndexOf(_secrets.First(s => s.Id == _editingSecret.Id));
            _secrets[index] = _editingSecret with
            {
                Key = key,
                Value = value,
                Description = string.IsNullOrWhiteSpace(description) ? null : description,
                ModifiedAt = DateTime.UtcNow
            };
        }
        else
        {
            // Add new
            var newSecret = new SecretEntry(
                Guid.NewGuid(),
                key,
                value,
                string.IsNullOrWhiteSpace(description) ? null : description,
                DateTime.UtcNow,
                null);

            _secrets.Add(newSecret);
        }

        await SaveSecretsAsync();
        UpdateSecretCount();
        RefreshList();

        pnlDialog.Visibility = Visibility.Collapsed;
        _editingSecret = null;
    }

    private void RefreshList()
    {
        var currentSearch = txtSearch.Text;
        lstSecrets.ItemsSource = null;

        if (string.IsNullOrWhiteSpace(currentSearch))
        {
            lstSecrets.ItemsSource = _secrets;
        }
        else
        {
            SearchBox_TextChanged(txtSearch, null!);
        }
    }

    #region API Keys Management

    private ObservableCollection<SecretSelectionItem> _secretSelectionItems = [];

    private void ApiKeysButton_Click(object sender, RoutedEventArgs e)
    {
        // Cargar lista de secretos para checkboxes
        _secretSelectionItems = new ObservableCollection<SecretSelectionItem>(
            _secrets.Select(s => new SecretSelectionItem { Key = s.Key, IsSelected = false }));
        lstSecretsCheckboxes.ItemsSource = _secretSelectionItems;

        // Reset form
        lstApiKeys.ItemsSource = _apiKeys;
        pnlGeneratedKey.Visibility = Visibility.Collapsed;
        txtNewKeyName.Text = "";
        txtSecretFilter.Text = "";
        rbFullAccess.IsChecked = true;
        chkCanList.IsChecked = false;
        pnlSecretsSelection.Visibility = Visibility.Collapsed;
        pnlApiKeys.Visibility = Visibility.Visible;
    }

    private void AccessLevel_Changed(object sender, RoutedEventArgs e)
    {
        if (pnlSecretsSelection == null) return;
        
        pnlSecretsSelection.Visibility = rbRestrictedAccess.IsChecked == true 
            ? Visibility.Visible 
            : Visibility.Collapsed;
    }

    private void SecretFilter_TextChanged(object sender, TextChangedEventArgs e)
    {
        var filter = txtSecretFilter.Text.Trim();
        
        if (string.IsNullOrEmpty(filter))
        {
            lstSecretsCheckboxes.ItemsSource = _secretSelectionItems;
        }
        else
        {
            var filtered = _secretSelectionItems
                .Where(s => s.Key.Contains(filter, StringComparison.OrdinalIgnoreCase))
                .ToList();
            lstSecretsCheckboxes.ItemsSource = filtered;
        }
    }

    private void SelectAllSecrets_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in _secretSelectionItems)
        {
            item.IsSelected = true;
        }
    }

    private void SelectNoSecrets_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in _secretSelectionItems)
        {
            item.IsSelected = false;
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
                message = $"Access Level: Full Access\n\nCan access ALL secrets.";
                NotificationService.ShowInfo($"API Key: {apiKey.Name}", message, 8);
            }
            else
            {
                var secretsCount = permissions.AllowedSecrets.Count;
                var secretsList = secretsCount > 0 
                    ? string.Join(", ", permissions.AllowedSecrets.Take(5)) + (secretsCount > 5 ? $" (+{secretsCount - 5} more)" : "")
                    : "(none)";

                message = $"Access: Restricted\nSecrets: {secretsList}\nCan List: {(permissions.CanList ? "Yes" : "No")}";
                NotificationService.ShowInfo($"API Key: {apiKey.Name}", message, 10);
            }
        }
    }

    private async void GenerateApiKeyButton_Click(object sender, RoutedEventArgs e)
    {
        var name = txtNewKeyName.Text.Trim();

        if (string.IsNullOrWhiteSpace(name))
        {
            NotificationService.ShowWarning("Validation", "Please enter a name for the API Key.");
            return;
        }

        // Determinar nivel de acceso y permisos
        AccessLevel accessLevel;
        List<string> allowedSecrets = [];
        List<string> allowedPrefixes = [];
        bool canList;

        if (rbFullAccess.IsChecked == true)
        {
            accessLevel = AccessLevel.Full;
            canList = true;
        }
        else
        {
            accessLevel = AccessLevel.Restricted;
            
            // Obtener secretos seleccionados de los checkboxes
            allowedSecrets = _secretSelectionItems
                .Where(s => s.IsSelected)
                .Select(s => s.Key)
                .ToList();

            canList = chkCanList.IsChecked == true;

            // Validar que tenga al menos un secreto seleccionado
            if (allowedSecrets.Count == 0)
            {
                NotificationService.ShowWarning("Validation", "Please select at least one secret for this API Key to access.");
                return;
            }
        }

        var permissions = new ApiKeyPermissions(accessLevel, allowedSecrets, allowedPrefixes, canList);

        // Generar nueva API Key
        var (apiKey, keyHash) = ApiKeyService.GenerateApiKey();

        var newApiKey = new ApiKeyEntry(
            Guid.NewGuid(),
            name,
            keyHash,
            $"Generated on {DateTime.Now:yyyy-MM-dd HH:mm}",
            DateTime.UtcNow,
            null,
            true,
            permissions);

        _apiKeys.Add(newApiKey);
        await SaveApiKeysAsync();

        // Actualizar servidor
        _secretServer.UpdateApiKeys(_apiKeys);
        _secretServer.RequireAuthentication = true;

        // Mostrar la API Key generada
        txtGeneratedKey.Text = apiKey;
        pnlGeneratedKey.Visibility = Visibility.Visible;
        
        // Limpiar formulario
        txtNewKeyName.Text = "";
        txtSecretFilter.Text = "";
        rbFullAccess.IsChecked = true;
        foreach (var item in _secretSelectionItems)
        {
            item.IsSelected = false;
        }
        chkCanList.IsChecked = false;

        // Refrescar lista
        lstApiKeys.ItemsSource = null;
        lstApiKeys.ItemsSource = _apiKeys;

        UpdateStatusBar();
    }

    private void CopyApiKeyButton_Click(object sender, RoutedEventArgs e)
    {
        Clipboard.SetText(txtGeneratedKey.Text);
        NotificationService.ShowSuccess("Copied", "API Key copied to clipboard. Store it securely - it won't be shown again.", 5);
        pnlGeneratedKey.Visibility = Visibility.Collapsed;
    }

    private async void DeleteApiKeyButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: Guid id })
        {
            var apiKey = _apiKeys.FirstOrDefault(k => k.Id == id);
            if (apiKey == null) return;

            var confirmed = await DialogService.ConfirmDangerousAsync(
                "Revoke API Key",
                $"Are you sure you want to revoke the API Key '{apiKey.Name}'?\n\nApplications using this key will lose access immediately.",
                "Revoke",
                "Cancel");

            if (confirmed)
            {
                _apiKeys.Remove(apiKey);
                await SaveApiKeysAsync();

                // Actualizar servidor
                _secretServer.UpdateApiKeys(_apiKeys);
                _secretServer.RequireAuthentication = _apiKeys.Count > 0;

                // Refrescar lista
                lstApiKeys.ItemsSource = null;
                lstApiKeys.ItemsSource = _apiKeys;

                UpdateStatusBar();
                
                NotificationService.ShowSuccess("Revoked", $"API Key '{apiKey.Name}' has been revoked.", 4);
            }
        }
    }

    private void CloseApiKeysButton_Click(object sender, RoutedEventArgs e)
    {
        pnlApiKeys.Visibility = Visibility.Collapsed;
        pnlGeneratedKey.Visibility = Visibility.Collapsed;
    }

    #endregion

    #region Audit Log

    private void AuditLogButton_Click(object sender, RoutedEventArgs e)
    {
        RefreshAuditLog();
        pnlAuditLog.Visibility = Visibility.Visible;
    }

    private void RefreshAuditLog()
    {
        // Obtener estadísticas
        var stats = _secretServer.AuditService.GetStatistics();
        txtTotalRequests.Text = stats.TotalRequests.ToString();
        txtSuccessRequests.Text = stats.SuccessfulRequests.ToString();
        txtFailedRequests.Text = stats.FailedRequests.ToString();
        txtUniqueClients.Text = stats.UniqueApiKeys.ToString();

        // Obtener logs recientes
        var logs = _secretServer.AuditService.GetRecentLogs(200);
        lstAuditLogs.ItemsSource = logs;
    }

    private void RefreshAuditLogButton_Click(object sender, RoutedEventArgs e)
    {
        RefreshAuditLog();
    }

    private void CloseAuditLogButton_Click(object sender, RoutedEventArgs e)
    {
        pnlAuditLog.Visibility = Visibility.Collapsed;
    }

    #endregion

    #region Backup (Export/Import)

    private readonly VaultExportService _exportService = new();

    private void BackupButton_Click(object sender, RoutedEventArgs e)
    {
        txtExportPassword.Password = "";
        txtExportPasswordConfirm.Password = "";
        txtImportPassword.Password = "";
        chkOverwriteExisting.IsChecked = false;
        chkImportApiKeys.IsChecked = true;
        pnlBackup.Visibility = Visibility.Visible;
    }

    private void CloseBackupButton_Click(object sender, RoutedEventArgs e)
    {
        pnlBackup.Visibility = Visibility.Collapsed;
    }

    private async void ExportVaultButton_Click(object sender, RoutedEventArgs e)
    {
        var password = txtExportPassword.Password;
        var confirmPassword = txtExportPasswordConfirm.Password;

        // Validaciones
        if (string.IsNullOrWhiteSpace(password))
        {
            NotificationService.ShowWarning("Validation", "Please enter an export password.");
            return;
        }

        if (password.Length < 8)
        {
            NotificationService.ShowWarning("Validation", "Export password must be at least 8 characters.");
            return;
        }

        if (password != confirmPassword)
        {
            NotificationService.ShowWarning("Validation", "Passwords do not match.");
            return;
        }

        // Seleccionar archivo de destino
        var saveDialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Export Vault",
            Filter = "Arca Vault Backup (*.arcavault)|*.arcavault",
            DefaultExt = ".arcavault",
            FileName = $"arca-backup-{DateTime.Now:yyyy-MM-dd}"
        };

        if (saveDialog.ShowDialog() != true)
            return;

        try
        {
            await _exportService.ExportAsync(_secrets, _apiKeys, password, saveDialog.FileName);
            
            NotificationService.ShowSuccess("Export Complete", 
                $"Vault exported successfully.\n{_secrets.Count} secrets, {_apiKeys.Count} API Keys.", 5);
            
            pnlBackup.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            NotificationService.ShowError("Export Failed", $"Failed to export vault: {ex.Message}");
        }
    }

    private async void ImportVaultButton_Click(object sender, RoutedEventArgs e)
    {
        var password = txtImportPassword.Password;

        if (string.IsNullOrWhiteSpace(password))
        {
            NotificationService.ShowWarning("Validation", "Please enter the import password.");
            return;
        }

        // Seleccionar archivo
        var openDialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Import Vault",
            Filter = "Arca Vault Backup (*.arcavault)|*.arcavault",
            DefaultExt = ".arcavault"
        };

        if (openDialog.ShowDialog() != true)
            return;

        try
        {
            // Verificar que sea un archivo válido
            if (!await _exportService.IsValidExportFileAsync(openDialog.FileName))
            {
                NotificationService.ShowError("Invalid File", "The selected file is not a valid Arca vault backup.");
                return;
            }

            // Cargar datos
            var exportData = await _exportService.LoadExportFileAsync(openDialog.FileName, password);
            if (exportData == null)
            {
                NotificationService.ShowError("Import Failed", "Failed to read the backup file.");
                return;
            }

            var overwrite = chkOverwriteExisting.IsChecked == true;
            var importApiKeys = chkImportApiKeys.IsChecked == true;

            int secretsImported = 0, secretsSkipped = 0;
            int apiKeysImported = 0, apiKeysSkipped = 0;

            // Importar secretos
            foreach (var secret in exportData.Secrets)
            {
                var existing = _secrets.FirstOrDefault(s => s.Key.Equals(secret.Key, StringComparison.OrdinalIgnoreCase));
                
                if (existing != null)
                {
                    if (overwrite)
                    {
                        var index = _secrets.IndexOf(existing);
                        _secrets[index] = existing with
                        {
                            Value = secret.Value,
                            Description = secret.Description,
                            ModifiedAt = DateTime.UtcNow
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
                        secret.Description,
                        DateTime.UtcNow,
                        null));
                    secretsImported++;
                }
            }

            // Importar API Keys (sin el hash, solo como referencia)
            if (importApiKeys)
            {
                foreach (var apiKey in exportData.ApiKeys)
                {
                    var existing = _apiKeys.FirstOrDefault(k => k.Name.Equals(apiKey.Name, StringComparison.OrdinalIgnoreCase));
                    
                    if (existing != null)
                    {
                        apiKeysSkipped++;
                    }
                    else
                    {
                        // Crear API Key inactiva (necesita regenerarse)
                        var level = Enum.TryParse<AccessLevel>(apiKey.AccessLevel, out var parsed) 
                            ? parsed 
                            : AccessLevel.Restricted;
                        
                        var permissions = new ApiKeyPermissions(
                            level,
                            apiKey.AllowedSecrets,
                            [],
                            apiKey.CanList);

                        _apiKeys.Add(new ApiKeyEntry(
                            Guid.NewGuid(),
                            $"{apiKey.Name} (imported)",
                            "", // Sin hash - necesita regenerarse
                            apiKey.Description,
                            DateTime.UtcNow,
                            null,
                            false, // Inactiva
                            permissions));
                        apiKeysImported++;
                    }
                }
            }

            // Guardar cambios
            await SaveSecretsAsync();
            await SaveApiKeysAsync();

            UpdateSecretCount();
            RefreshList();

            var message = $"Imported: {secretsImported} secrets";
            if (secretsSkipped > 0) message += $", {secretsSkipped} skipped";
            if (importApiKeys) message += $"\nAPI Keys: {apiKeysImported} imported";
            if (apiKeysSkipped > 0) message += $", {apiKeysSkipped} skipped";
            message += $"\n\nFrom: {exportData.ExportedFrom} ({exportData.ExportedAt:g})";

            NotificationService.ShowSuccess("Import Complete", message, 8);
            
            pnlBackup.Visibility = Visibility.Collapsed;
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("password"))
        {
            NotificationService.ShowError("Wrong Password", "The password is incorrect or the file is corrupted.");
        }
        catch (Exception ex)
        {
            NotificationService.ShowError("Import Failed", $"Failed to import vault: {ex.Message}");
        }
    }

    #endregion
}