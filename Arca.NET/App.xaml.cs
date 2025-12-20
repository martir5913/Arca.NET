using Arca.NET.Services;
using Arca.NET.Views;
using System.Windows;
using Application = System.Windows.Application;

namespace Arca.NET;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    private TrayIconService? _trayIcon;
    private LoginWindow? _loginWindow;
    private MainWindow? _mainWindow;

    // Estado global
    public static bool IsVaultUnlocked { get; set; }
    public static int SecretCount { get; set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Inicializar el icono de la bandeja del sistema
        _trayIcon = new TrayIconService();
        _trayIcon.ShowWindowRequested += OnShowWindowRequested;
        _trayIcon.LockVaultRequested += OnLockVaultRequested;
        _trayIcon.ExitRequested += OnExitRequested;
        _trayIcon.Show();

        // Mostrar ventana de login
        ShowLoginWindow();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _trayIcon?.Dispose();
        base.OnExit(e);
    }

    /// <summary>
    /// Muestra la ventana de login.
    /// </summary>
    public void ShowLoginWindow()
    {
        // Cerrar MainWindow si está abierta
        if (_mainWindow != null)
        {
            _mainWindow.Close();
            _mainWindow = null;
        }

        _loginWindow = new LoginWindow();
        _loginWindow.Closing += OnLoginWindowClosing;
        _loginWindow.Show();
        _loginWindow.Activate();

        IsVaultUnlocked = false;
        _trayIcon?.UpdateStatus(false, 0);
    }

    /// <summary>
    /// Muestra la ventana principal después de desbloquear.
    /// </summary>
    /// <param name="minimizeToTray">Si es true, minimiza a la bandeja después de crear la ventana</param>
    public void ShowMainWindow(
        byte[] derivedKey,
        Arca.Core.Interfaces.IVaultRepository vaultRepository,
        Arca.Core.Interfaces.IAesGcmService aesGcmService,
        Arca.Core.Interfaces.IKeyDerivationService keyDerivationService,
        bool minimizeToTray = true)
    {
        // Cerrar LoginWindow
        if (_loginWindow != null)
        {
            _loginWindow.Closing -= OnLoginWindowClosing;
            _loginWindow.Close();
            _loginWindow = null;
        }

        _mainWindow = new MainWindow(derivedKey, vaultRepository, aesGcmService, keyDerivationService);
        _mainWindow.Closing += OnMainWindowClosing;

        IsVaultUnlocked = true;
        _trayIcon?.UpdateStatus(true, SecretCount);

        if (minimizeToTray)
        {
            // No mostrar la ventana, solo inicializarla y minimizar a bandeja
            _mainWindow.Show();
            _mainWindow.Hide();
            _trayIcon?.ShowNotification("Arca", "Vault desbloqueado. Ejecutándose en segundo plano.\nHaz doble clic en el icono para abrir.");
        }
        else
        {
            _mainWindow.Show();
            _mainWindow.Activate();
            _trayIcon?.ShowNotification("Arca", "Vault desbloqueado.");
        }
    }

    /// <summary>
    /// Actualiza el conteo de secretos.
    /// </summary>
    public void UpdateSecretCount(int count)
    {
        SecretCount = count;
        _trayIcon?.UpdateStatus(IsVaultUnlocked, count);
    }

    /// <summary>
    /// Minimiza la aplicación a la bandeja del sistema.
    /// </summary>
    /// <param name="showNotification">Si es true, muestra notificación al usuario</param>
    public void MinimizeToTray(bool showNotification = true)
    {
        _mainWindow?.Hide();
        _loginWindow?.Hide();

        if (showNotification)
        {
            if (IsVaultUnlocked)
            {
                _trayIcon?.ShowNotification("Arca", "Ejecutándose en segundo plano.\nHaz doble clic en el icono para abrir.");
            }
            else
            {
                _trayIcon?.ShowNotification("Arca", "Vault bloqueado. Ejecutándose en segundo plano.\nHaz doble clic en el icono para desbloquear.");
            }
        }
    }

    private void OnShowWindowRequested(object? sender, EventArgs e)
    {
        if (IsVaultUnlocked && _mainWindow != null)
        {
            _mainWindow.Show();
            _mainWindow.WindowState = WindowState.Normal;
            _mainWindow.Activate();
        }
        else
        {
            if (_loginWindow == null)
            {
                ShowLoginWindow();
            }
            else
            {
                _loginWindow.Show();
                _loginWindow.WindowState = WindowState.Normal;
                _loginWindow.Activate();
            }
        }
    }

    private void OnLockVaultRequested(object? sender, EventArgs e)
    {
        // Cerrar MainWindow y volver a Login
        ShowLoginWindow();
        _trayIcon?.ShowNotification("Arca", "Vault bloqueado.");
    }

    private void OnExitRequested(object? sender, EventArgs e)
    {
        _trayIcon?.Hide();
        Shutdown();
    }

    private void OnLoginWindowClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        // Si no está desbloqueado, minimizar a bandeja en lugar de cerrar
        if (!IsVaultUnlocked)
        {
            e.Cancel = true;
            MinimizeToTray();
        }
    }

    private void OnMainWindowClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        // Minimizar a bandeja en lugar de cerrar
        e.Cancel = true;
        MinimizeToTray();
    }
}
