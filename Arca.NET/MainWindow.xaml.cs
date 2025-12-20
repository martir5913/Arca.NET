using Arca.Core.Entities;
using Arca.Core.Interfaces;
using Arca.Core.Security;
using Arca.NET.Services;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using Application = System.Windows.Application;
using Button = System.Windows.Controls.Button;
using Clipboard = System.Windows.Clipboard;
using MessageBox = System.Windows.MessageBox;

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
        await LoadSecretsAsync();
        await LoadApiKeysAsync();

        // Iniciar servidor embebido para que otras apps puedan obtener secretos
        _secretServer.UpdateSecrets(_secrets);
        _secretServer.UpdateApiKeys(_apiKeys);
        _secretServer.RequireAuthentication = _apiKeys.Count > 0; // Solo requerir auth si hay keys configuradas
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
            UpdateSecretCount();

            // Actualizar servidor embebido
            _secretServer.UpdateSecrets(_secrets);

            // Actualizar App con el conteo
            if (Application.Current is App app)
            {
                app.UpdateSecretCount(_secrets.Count);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error loading secrets: {ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
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
            MessageBox.Show($"Error loading API keys: {ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task SaveSecretsAsync()
    {
        try
        {
            await _vaultRepository.SaveSecretsAsync(_secrets, _derivedKey);

            // Actualizar servidor embebido
            _secretServer.UpdateSecrets(_secrets);

            // Actualizar App con el conteo
            if (Application.Current is App app)
            {
                app.UpdateSecretCount(_secrets.Count);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error saving secrets: {ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
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
            MessageBox.Show($"Error saving API keys: {ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
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
            var result = MessageBox.Show(
                "Are you sure you want to delete this secret?",
                "Confirm Delete",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                var secret = _secrets.FirstOrDefault(s => s.Id == id);
                if (secret is not null)
                {
                    _secrets.Remove(secret);
                    await SaveSecretsAsync();
                    UpdateSecretCount();
                    RefreshList();
                }
            }
        }
    }

    private void CopyValueButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string value })
        {
            Clipboard.SetText(value);
            txtStatus.Text = "Value copied to clipboard!";

            // Reset status after 2 seconds
            var timer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(2)
            };
            timer.Tick += (s, args) =>
            {
                txtStatus.Text = "Vault unlocked - SDK server active";
                timer.Stop();
            };
            timer.Start();
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

    private void ApiKeysButton_Click(object sender, RoutedEventArgs e)
    {
        lstApiKeys.ItemsSource = _apiKeys;
        pnlGeneratedKey.Visibility = Visibility.Collapsed;
        txtNewKeyName.Text = "";
        pnlApiKeys.Visibility = Visibility.Visible;
    }

    private async void GenerateApiKeyButton_Click(object sender, RoutedEventArgs e)
    {
        var name = txtNewKeyName.Text.Trim();

        if (string.IsNullOrWhiteSpace(name))
        {
            MessageBox.Show("Please enter a name for the API Key.", "Validation",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // Generar nueva API Key
        var (apiKey, keyHash) = ApiKeyService.GenerateApiKey();

        var newApiKey = new ApiKeyEntry(
            Guid.NewGuid(),
            name,
            keyHash,
            $"Generated on {DateTime.Now:yyyy-MM-dd HH:mm}",
            DateTime.UtcNow,
            null,
            true);

        _apiKeys.Add(newApiKey);
        await SaveApiKeysAsync();

        // Actualizar servidor
        _secretServer.UpdateApiKeys(_apiKeys);
        _secretServer.RequireAuthentication = true;

        // Mostrar la API Key generada
        txtGeneratedKey.Text = apiKey;
        pnlGeneratedKey.Visibility = Visibility.Visible;
        txtNewKeyName.Text = "";

        // Refrescar lista
        lstApiKeys.ItemsSource = null;
        lstApiKeys.ItemsSource = _apiKeys;

        UpdateStatusBar();
    }

    private void CopyApiKeyButton_Click(object sender, RoutedEventArgs e)
    {
        Clipboard.SetText(txtGeneratedKey.Text);
        MessageBox.Show(
            "API Key copied to clipboard!\n\nStore it securely - it won't be shown again.",
            "Copied",
            MessageBoxButton.OK,
            MessageBoxImage.Information);

        pnlGeneratedKey.Visibility = Visibility.Collapsed;
    }

    private async void DeleteApiKeyButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: Guid id })
        {
            var apiKey = _apiKeys.FirstOrDefault(k => k.Id == id);
            if (apiKey == null) return;

            var result = MessageBox.Show(
                $"Are you sure you want to revoke the API Key '{apiKey.Name}'?\n\nApplications using this key will lose access.",
                "Confirm Revoke",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
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
}