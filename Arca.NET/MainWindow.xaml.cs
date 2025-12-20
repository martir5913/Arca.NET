using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using Arca.Core.Entities;
using Arca.Core.Interfaces;
using Arca.NET.Views;

namespace Arca.NET;

public partial class MainWindow : Window
{
    private readonly byte[] _derivedKey;
    private readonly IVaultRepository _vaultRepository;
    private readonly IAesGcmService _aesGcmService;
    private readonly IKeyDerivationService _keyDerivationService;

    private ObservableCollection<SecretEntry> _secrets = [];
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

        Loaded += MainWindow_Loaded;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        await LoadSecretsAsync();
    }

    private async Task LoadSecretsAsync()
    {
        try
        {
            var secrets = await _vaultRepository.LoadSecretsAsync(_derivedKey);
            _secrets = new ObservableCollection<SecretEntry>(secrets);
            lstSecrets.ItemsSource = _secrets;
            UpdateSecretCount();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error loading secrets: {ex.Message}", "Error", 
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task SaveSecretsAsync()
    {
        try
        {
            await _vaultRepository.SaveSecretsAsync(_secrets, _derivedKey);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error saving secrets: {ex.Message}", "Error", 
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void UpdateSecretCount()
    {
        txtSecretCount.Text = $"{_secrets.Count} secret{(_secrets.Count != 1 ? "s" : "")}";
    }

    private void LockButton_Click(object sender, RoutedEventArgs e)
    {
        // Clear sensitive data
        Array.Clear(_derivedKey, 0, _derivedKey.Length);

        var loginWindow = new LoginWindow();
        loginWindow.Show();
        Close();
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
            txtStatus.Text = "📋 Value copied to clipboard!";

            // Reset status after 2 seconds
            var timer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(2)
            };
            timer.Tick += (s, args) =>
            {
                txtStatus.Text = "🔓 Vault unlocked";
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
}