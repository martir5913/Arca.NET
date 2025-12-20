using Arca.Core.Entities;
using Arca.Core.Interfaces;
using Arca.Infrastructure.Persistence;
using Arca.Infrastructure.Security;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace Arca.NET.Views;

public partial class LoginWindow : Window
{
    private readonly IVaultRepository _vaultRepository;
    private readonly IKeyDerivationService _keyDerivationService;
    private readonly IAesGcmService _aesGcmService;

    public byte[]? DerivedKey { get; private set; }

    public LoginWindow()
    {
        InitializeComponent();

        // Initialize services
        _aesGcmService = new AesGcmService();
        _keyDerivationService = new KeyDerivationService();
        _vaultRepository = new BinaryVaultRepository(_aesGcmService);

        UpdateVaultStatus();
    }

    private void UpdateVaultStatus()
    {
        if (_vaultRepository.VaultExists())
        {
            VaultStatus.Text = $"Vault: {_vaultRepository.GetVaultPath()}";
            CreateVaultButton.Visibility = Visibility.Collapsed;
            UnlockButton.Content = "Unlock Vault";
        }
        else
        {
            VaultStatus.Text = "No vault found. Create one to get started.";
            CreateVaultButton.Visibility = Visibility.Visible;
            UnlockButton.Visibility = Visibility.Collapsed;
        }
    }

    private void PasswordBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            UnlockButton_Click(sender, e);
        }
    }

    private async void UnlockButton_Click(object sender, RoutedEventArgs e)
    {
        var password = PasswordBox.Password;

        if (string.IsNullOrWhiteSpace(password))
        {
            ShowError(ErrorMessage, "Please enter your master password.");
            return;
        }

        ShowLoading("Unlocking vault...");

        try
        {
            var metadata = await _vaultRepository.LoadMetadataAsync();
            if (metadata is null)
            {
                HideLoading();
                ShowError(ErrorMessage, "Vault not found.");
                return;
            }

            // Derive key from password
            var derivedKey = await Task.Run(() =>
                _keyDerivationService.DeriveKey(password, metadata.Salt));

            // Try to load secrets to verify the password
            await _vaultRepository.LoadSecretsAsync(derivedKey);

            // Success - store key and open main window
            DerivedKey = derivedKey;
            HideLoading();

            var mainWindow = new MainWindow(derivedKey, _vaultRepository, _aesGcmService, _keyDerivationService);
            mainWindow.Show();
            Close();
        }
        catch (System.Security.Cryptography.AuthenticationTagMismatchException)
        {
            HideLoading();
            ShowError(ErrorMessage, "Invalid password. Please try again.");
            PasswordBox.Clear();
            PasswordBox.Focus();
        }
        catch (Exception ex)
        {
            HideLoading();
            ShowError(ErrorMessage, $"Error: {ex.Message}");
        }
    }

    private void CreateVaultButton_Click(object sender, RoutedEventArgs e)
    {
        LoginPanel.Visibility = Visibility.Collapsed;
        CreateVaultPanel.Visibility = Visibility.Visible;
        NewPasswordBox.Focus();
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        CreateVaultPanel.Visibility = Visibility.Collapsed;
        LoginPanel.Visibility = Visibility.Visible;
        NewPasswordBox.Clear();
        ConfirmPasswordBox.Clear();
        CreateErrorMessage.Visibility = Visibility.Collapsed;
    }

    private void NewPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        var password = NewPasswordBox.Password;
        var (strength, color, text) = CalculatePasswordStrength(password);

        PasswordStrengthBar.Value = strength;
        PasswordStrengthBar.Maximum = 100;
        PasswordStrengthBar.Foreground = new SolidColorBrush(color);
        PasswordStrengthText.Text = text;
    }

    private static (int strength, Color color, string text) CalculatePasswordStrength(string password)
    {
        if (string.IsNullOrEmpty(password))
            return (0, Colors.Gray, "");

        int strength = 0;

        if (password.Length >= 8) strength += 20;
        if (password.Length >= 12) strength += 20;
        if (password.Length >= 16) strength += 10;
        if (password.Any(char.IsUpper)) strength += 15;
        if (password.Any(char.IsLower)) strength += 10;
        if (password.Any(char.IsDigit)) strength += 15;
        if (password.Any(c => !char.IsLetterOrDigit(c))) strength += 20;

        return strength switch
        {
            < 30 => (strength, Color.FromRgb(255, 107, 107), "Weak"),
            < 60 => (strength, Color.FromRgb(255, 170, 0), "Fair"),
            < 80 => (strength, Color.FromRgb(78, 205, 196), "Good"),
            _ => (strength, Color.FromRgb(46, 213, 115), "Strong")
        };
    }

    private async void ConfirmCreateButton_Click(object sender, RoutedEventArgs e)
    {
        var password = NewPasswordBox.Password;
        var confirmPassword = ConfirmPasswordBox.Password;

        // Validations
        if (string.IsNullOrWhiteSpace(password))
        {
            ShowError(CreateErrorMessage, "Please enter a password.");
            return;
        }

        if (password.Length < 8)
        {
            ShowError(CreateErrorMessage, "Password must be at least 8 characters.");
            return;
        }

        if (password != confirmPassword)
        {
            ShowError(CreateErrorMessage, "Passwords do not match.");
            return;
        }

        ShowLoading("Creating vault...");

        try
        {
            // Generate salt and derive key
            var salt = _keyDerivationService.GenerateSalt();
            var derivedKey = await Task.Run(() =>
                _keyDerivationService.DeriveKey(password, salt));

            // Create vault metadata
            var metadata = new VaultMetadata(salt, Version: 1, CreatedAt: DateTime.UtcNow);

            // Create the vault file
            await _vaultRepository.CreateVaultAsync(metadata, derivedKey);

            HideLoading();

            MessageBox.Show(
                $"Vault created successfully!\n\nLocation: {_vaultRepository.GetVaultPath()}",
                "Success",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            // Open main window
            DerivedKey = derivedKey;
            var mainWindow = new MainWindow(derivedKey, _vaultRepository, _aesGcmService, _keyDerivationService);
            mainWindow.Show();
            Close();
        }
        catch (Exception ex)
        {
            HideLoading();
            ShowError(CreateErrorMessage, $"Error creating vault: {ex.Message}");
        }
    }

    private void ShowError(System.Windows.Controls.TextBlock errorBlock, string message)
    {
        errorBlock.Text = message;
        errorBlock.Visibility = Visibility.Visible;
    }

    private void ShowLoading(string text)
    {
        LoadingText.Text = text;
        LoadingOverlay.Visibility = Visibility.Visible;
        IsEnabled = false;
    }

    private void HideLoading()
    {
        LoadingOverlay.Visibility = Visibility.Collapsed;
        IsEnabled = true;
    }
}
