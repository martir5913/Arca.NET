using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace Arca.NET.Controls;

public enum NotificationType
{
    Info,
    Success,
    Warning,
    Error
}

public class NotificationPanel : Border
{
    private TextBlock? _titleBlock;
    private TextBlock? _messageBlock;
    private System.Windows.Controls.Button? _closeButton;
    private System.Windows.Controls.Button? _actionButton;
    private DispatcherTimer? _autoCloseTimer;
    private Action? _onAction;

    public NotificationPanel()
    {
        InitializeControl();
    }

    private void InitializeControl()
    {
        // Estilo base del panel
        Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(22, 33, 62)); // #16213e
        CornerRadius = new CornerRadius(8);
        Padding = new Thickness(15);
        Margin = new Thickness(0, 0, 0, 10);
        BorderThickness = new Thickness(1);
        MaxWidth = 400;
        HorizontalAlignment = System.Windows.HorizontalAlignment.Right;
        VerticalAlignment = System.Windows.VerticalAlignment.Top;

        // Grid principal
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // Panel de contenido
        var contentPanel = new StackPanel();

        _titleBlock = new TextBlock
        {
            FontWeight = FontWeights.Bold,
            FontSize = 14,
            Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(232, 232, 232)),
            Margin = new Thickness(0, 0, 0, 5)
        };

        _messageBlock = new TextBlock
        {
            FontSize = 12,
            Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(136, 136, 136)),
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 320
        };

        _actionButton = new System.Windows.Controls.Button
        {
            Visibility = Visibility.Collapsed,
            Margin = new Thickness(0, 10, 0, 0),
            Padding = new Thickness(12, 6, 12, 6),
            FontSize = 11,
            Cursor = System.Windows.Input.Cursors.Hand,
            Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(233, 69, 96)),
            Foreground = System.Windows.Media.Brushes.White,
            BorderThickness = new Thickness(0)
        };
        _actionButton.Click += (s, e) => _onAction?.Invoke();

        contentPanel.Children.Add(_titleBlock);
        contentPanel.Children.Add(_messageBlock);
        contentPanel.Children.Add(_actionButton);

        // Botón de cerrar
        _closeButton = new System.Windows.Controls.Button
        {
            Content = "✕",
            Background = System.Windows.Media.Brushes.Transparent,
            Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(136, 136, 136)),
            BorderThickness = new Thickness(0),
            FontSize = 14,
            Padding = new Thickness(5),
            Cursor = System.Windows.Input.Cursors.Hand,
            VerticalAlignment = System.Windows.VerticalAlignment.Top
        };
        _closeButton.Click += (s, e) => Close();

        Grid.SetColumn(contentPanel, 0);
        Grid.SetColumn(_closeButton, 1);

        grid.Children.Add(contentPanel);
        grid.Children.Add(_closeButton);

        Child = grid;

        // Inicialmente oculto
        Visibility = Visibility.Collapsed;
        Opacity = 0;
    }

    public void Show(string title, string message, NotificationType type, int autoCloseSeconds = 0, string? actionText = null, Action? onAction = null)
    {
        _titleBlock!.Text = title;
        _messageBlock!.Text = message;
        _onAction = onAction;

        // Configurar botón de acción
        if (!string.IsNullOrEmpty(actionText) && onAction != null)
        {
            _actionButton!.Content = actionText;
            _actionButton.Visibility = Visibility.Visible;
        }
        else
        {
            _actionButton!.Visibility = Visibility.Collapsed;
        }

        // Aplicar estilo según tipo
        ApplyStyle(type);

        // Mostrar con animación
        Visibility = Visibility.Visible;
        var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200));
        BeginAnimation(OpacityProperty, fadeIn);

        // Auto-cerrar si se especifica
        if (autoCloseSeconds > 0)
        {
            _autoCloseTimer?.Stop();
            _autoCloseTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(autoCloseSeconds)
            };
            _autoCloseTimer.Tick += (s, e) =>
            {
                _autoCloseTimer.Stop();
                Close();
            };
            _autoCloseTimer.Start();
        }
    }

    private void ApplyStyle(NotificationType type)
    {
        System.Windows.Media.Color borderColor;
        string icon;

        switch (type)
        {
            case NotificationType.Success:
                borderColor = System.Windows.Media.Color.FromRgb(46, 213, 115); // Verde
                icon = "✓ ";
                break;
            case NotificationType.Warning:
                borderColor = System.Windows.Media.Color.FromRgb(255, 165, 2); // Naranja
                icon = "⚠ ";
                break;
            case NotificationType.Error:
                borderColor = System.Windows.Media.Color.FromRgb(255, 107, 107); // Rojo
                icon = "✕ ";
                break;
            default: // Info
                borderColor = System.Windows.Media.Color.FromRgb(78, 205, 196); // Cyan
                icon = "ℹ ";
                break;
        }

        BorderBrush = new System.Windows.Media.SolidColorBrush(borderColor);
        _titleBlock!.Text = icon + _titleBlock.Text;
        _titleBlock.Foreground = new System.Windows.Media.SolidColorBrush(borderColor);
    }

    public void Close()
    {
        _autoCloseTimer?.Stop();

        var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(200));
        fadeOut.Completed += (s, e) => Visibility = Visibility.Collapsed;
        BeginAnimation(OpacityProperty, fadeOut);
    }
}

public static class NotificationService
{
    private static System.Windows.Controls.Panel? _container;
    private static readonly List<NotificationPanel> _activeNotifications = [];

    public static void Initialize(System.Windows.Controls.Panel container)
    {
        _container = container;
    }

    public static void ShowInfo(string title, string message, int autoCloseSeconds = 5)
    {
        Show(title, message, NotificationType.Info, autoCloseSeconds);
    }

    public static void ShowSuccess(string title, string message, int autoCloseSeconds = 4)
    {
        Show(title, message, NotificationType.Success, autoCloseSeconds);
    }

    public static void ShowWarning(string title, string message, int autoCloseSeconds = 6)
    {
        Show(title, message, NotificationType.Warning, autoCloseSeconds);
    }

    public static void ShowError(string title, string message, int autoCloseSeconds = 0)
    {
        Show(title, message, NotificationType.Error, autoCloseSeconds);
    }

    public static void ShowWithAction(string title, string message, NotificationType type, string actionText, Action onAction, int autoCloseSeconds = 0)
    {
        Show(title, message, type, autoCloseSeconds, actionText, onAction);
    }

    private static void Show(string title, string message, NotificationType type, int autoCloseSeconds, string? actionText = null, Action? onAction = null)
    {
        if (_container == null)
            return;

        // Crear nueva notificación
        var notification = new NotificationPanel();

        // Agregar al contenedor
        _container.Children.Add(notification);
        _activeNotifications.Add(notification);

        // Mostrar
        notification.Show(title, message, type, autoCloseSeconds, actionText, onAction);

        // Limpiar cuando se cierre
        notification.IsVisibleChanged += (s, e) =>
        {
            if (notification.Visibility == Visibility.Collapsed)
            {
                _container.Children.Remove(notification);
                _activeNotifications.Remove(notification);
            }
        };

        // Limitar cantidad de notificaciones activas
        while (_activeNotifications.Count > 5)
        {
            _activeNotifications[0].Close();
        }
    }

    public static void CloseAll()
    {
        foreach (var notification in _activeNotifications.ToList())
        {
            notification.Close();
        }
    }
}
