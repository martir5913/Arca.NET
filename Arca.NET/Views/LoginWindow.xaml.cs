using Arca.Core.Entities;
using Arca.Core.Interfaces;
using Arca.Infrastructure.Persistence;
using Arca.Infrastructure.Security;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Application = System.Windows.Application;
using Color = System.Windows.Media.Color;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MessageBox = System.Windows.MessageBox;

namespace Arca.NET.Views;

public partial class LoginWindow : Window
{
    private readonly IVaultRepository _vaultRepository;
    private readonly IKeyDerivationService _keyDerivationService;
    private readonly IAesGcmService _aesGcmService;
    private readonly string? _customVaultPath;

    public byte[]? DerivedKey { get; private set; }

    public LoginWindow() : this(null)
    {
    }

    public LoginWindow(string? vaultPath)
    {
        InitializeComponent();

        _customVaultPath = vaultPath;

        // Initialize services
        _aesGcmService = new AesGcmService();
        _keyDerivationService = new KeyDerivationService();
        _vaultRepository = new BinaryVaultRepository(_aesGcmService, vaultPath);

        UpdateVaultStatus();
    }

    private void UpdateVaultStatus()
    {
        if (_vaultRepository.VaultExists())
        {
            var path = _vaultRepository.GetVaultPath();
            // Mostrar path abreviado si es muy largo
            var displayPath = path.Length > 45
                ? "..." + path.Substring(path.Length - 42)
                : path;
            VaultStatus.Text = displayPath;
            
            // Mostrar sección de contraseña
            PasswordSection.Visibility = Visibility.Visible;
            NoVaultMessage.Visibility = Visibility.Collapsed;
            CreateVaultButton.Visibility = Visibility.Collapsed;
            
            PasswordBox.Focus();
        }
        else
        {
            VaultStatus.Text = "";
            
            // Ocultar sección de contraseña, mostrar mensaje
            PasswordSection.Visibility = Visibility.Collapsed;
            NoVaultMessage.Visibility = Visibility.Visible;
            CreateVaultButton.Visibility = Visibility.Visible;
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

            // Success - store key and open main window via App
            DerivedKey = derivedKey;
            HideLoading();

            // Usar App para abrir MainWindow
            if (Application.Current is App app)
            {
                app.ShowMainWindow(derivedKey, _vaultRepository, _aesGcmService, _keyDerivationService);
            }
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

            // Open main window via App
            DerivedKey = derivedKey;
            if (Application.Current is App app)
            {
                app.ShowMainWindow(derivedKey, _vaultRepository, _aesGcmService, _keyDerivationService);
            }
        }
        catch (Exception ex)
        {
            HideLoading();
            ShowError(CreateErrorMessage, $"Error creating vault: {ex.Message}");
        }
    }

    private void OpenVaultButton_Click(object sender, RoutedEventArgs e)
    {
        var openDialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Open Existing Vault",
            Filter = "Arca Vault (*.vlt)|*.vlt|All Files (*.*)|*.*",
            DefaultExt = ".vlt"
        };

        if (openDialog.ShowDialog() != true)
            return;

        var vaultPath = openDialog.FileName;

        // Verificar que sea un vault válido
        try
        {
            // Crear nuevo repository apuntando al vault seleccionado
            var newRepository = new BinaryVaultRepository(_aesGcmService, vaultPath);

            if (!newRepository.VaultExists())
            {
                MessageBox.Show("The selected file is not a valid vault.", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // Verificar que sea un vault válido intentando leer metadata
            var metadata = newRepository.LoadMetadataAsync().GetAwaiter().GetResult();
            if (metadata == null)
            {
                MessageBox.Show("The selected file is not a valid Arca vault.", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // Reemplazar el repository actual con el nuevo
            // Necesitamos recrear la ventana con el nuevo path
            if (Application.Current is App app)
            {
                app.OpenVaultFromPath(vaultPath);
            }
        }
        catch (InvalidDataException)
        {
            MessageBox.Show("The selected file is not a valid Arca vault.", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error opening vault: {ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
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
