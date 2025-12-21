using System.Windows;
using System.Windows.Controls;

namespace Arca.NET.Controls;

public class ConfirmDialog : Border
{
    private TextBlock? _titleBlock;
    private TextBlock? _messageBlock;
    private System.Windows.Controls.Button? _confirmButton;
    private System.Windows.Controls.Button? _cancelButton;
    private TaskCompletionSource<bool>? _resultSource;

    public ConfirmDialog()
    {
        InitializeControl();
    }

    private void InitializeControl()
    {
        Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(204, 0, 0, 0));
        Visibility = Visibility.Collapsed;

        var dialogBorder = new Border
        {
            Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(26, 26, 46)),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(25),
            MinWidth = 350,
            MaxWidth = 450,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            VerticalAlignment = System.Windows.VerticalAlignment.Center
        };

        var stackPanel = new StackPanel();

        _titleBlock = new TextBlock
        {
            FontSize = 18,
            FontWeight = FontWeights.Bold,
            Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(233, 69, 96)),
            Margin = new Thickness(0, 0, 0, 15),
            TextWrapping = TextWrapping.Wrap
        };

        _messageBlock = new TextBlock
        {
            FontSize = 13,
            Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(200, 200, 200)),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 20)
        };

        var buttonPanel = new StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right
        };

        _cancelButton = new System.Windows.Controls.Button
        {
            Content = "Cancel",
            Padding = new Thickness(20, 10, 20, 10),
            FontSize = 13,
            Background = System.Windows.Media.Brushes.Transparent,
            Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(136, 136, 136)),
            BorderThickness = new Thickness(0),
            Cursor = System.Windows.Input.Cursors.Hand,
            Margin = new Thickness(0, 0, 10, 0)
        };
        _cancelButton.Click += (s, e) => SetResult(false);

        _confirmButton = new System.Windows.Controls.Button
        {
            Content = "Confirm",
            Padding = new Thickness(20, 10, 20, 10),
            FontSize = 13,
            Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(233, 69, 96)),
            Foreground = System.Windows.Media.Brushes.White,
            BorderThickness = new Thickness(0),
            Cursor = System.Windows.Input.Cursors.Hand
        };
        _confirmButton.Click += (s, e) => SetResult(true);

        _cancelButton.Template = CreateButtonTemplate(false);
        _confirmButton.Template = CreateButtonTemplate(true);

        buttonPanel.Children.Add(_cancelButton);
        buttonPanel.Children.Add(_confirmButton);

        stackPanel.Children.Add(_titleBlock);
        stackPanel.Children.Add(_messageBlock);
        stackPanel.Children.Add(buttonPanel);

        dialogBorder.Child = stackPanel;
        Child = dialogBorder;
    }

    private static ControlTemplate CreateButtonTemplate(bool isPrimary)
    {
        var template = new ControlTemplate(typeof(System.Windows.Controls.Button));

        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
        border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(BackgroundProperty));
        border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(PaddingProperty));

        var contentPresenter = new FrameworkElementFactory(typeof(ContentPresenter));
        contentPresenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, System.Windows.HorizontalAlignment.Center);
        contentPresenter.SetValue(ContentPresenter.VerticalAlignmentProperty, System.Windows.VerticalAlignment.Center);

        border.AppendChild(contentPresenter);
        template.VisualTree = border;

        var trigger = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        if (isPrimary)
        {
            trigger.Setters.Add(new Setter(BackgroundProperty, new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 107, 107))));
        }
        else
        {
            trigger.Setters.Add(new Setter(BackgroundProperty, new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(42, 42, 78))));
        }
        template.Triggers.Add(trigger);

        return template;
    }

    public async Task<bool> ShowAsync(string title, string message, string confirmText = "Confirm", string cancelText = "Cancel", bool isDangerous = false)
    {
        _titleBlock!.Text = title;
        _messageBlock!.Text = message;
        _confirmButton!.Content = confirmText;
        _cancelButton!.Content = cancelText;

        if (isDangerous)
        {
            _confirmButton.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 107, 107));
        }
        else
        {
            _confirmButton.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(233, 69, 96));
        }

        _resultSource = new TaskCompletionSource<bool>();
        Visibility = Visibility.Visible;

        return await _resultSource.Task;
    }

    private void SetResult(bool result)
    {
        Visibility = Visibility.Collapsed;
        _resultSource?.TrySetResult(result);
    }
}

public static class DialogService
{
    private static ConfirmDialog? _dialog;
    private static System.Windows.Controls.Panel? _container;

    public static void Initialize(System.Windows.Controls.Panel container)
    {
        _container = container;
        _dialog = new ConfirmDialog
        {
            HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch,
            VerticalAlignment = System.Windows.VerticalAlignment.Stretch
        };

        _container.Children.Add(_dialog);
        Grid.SetRowSpan(_dialog, 10);
    }

    public static async Task<bool> ConfirmAsync(string title, string message, string confirmText = "Confirm", string cancelText = "Cancel")
    {
        if (_dialog == null) return false;
        return await _dialog.ShowAsync(title, message, confirmText, cancelText, false);
    }

    public static async Task<bool> ConfirmDangerousAsync(string title, string message, string confirmText = "Delete", string cancelText = "Cancel")
    {
        if (_dialog == null) return false;
        return await _dialog.ShowAsync(title, message, confirmText, cancelText, true);
    }
}
