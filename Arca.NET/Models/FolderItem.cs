using System.ComponentModel;
using System.Windows;
using System.Windows.Media;

namespace Arca.NET.Models;

public class FolderItem : INotifyPropertyChanged
{
    private bool _isSelected;
    private int _count;

    public string Name { get; set; } = string.Empty;
    public string? FolderKey { get; set; }
    public string Icon { get; set; } = "🗂️";
    public bool IsSpecial { get; set; }

    public Visibility DeleteVisibility => !IsSpecial ? Visibility.Visible : Visibility.Collapsed;

    public int Count
    {
        get => _count;
        set
        {
            if (_count != value)
            {
                _count = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Count)));
            }
        }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected != value)
            {
                _isSelected = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(BackgroundBrush)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ForegroundBrush)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FontWeight)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(BadgeBackgroundBrush)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(BadgeForegroundBrush)));
            }
        }
    }

    public System.Windows.Media.Brush BackgroundBrush => _isSelected
        ? new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#0f3460"))
        : System.Windows.Media.Brushes.Transparent;

    public System.Windows.Media.Brush ForegroundBrush => _isSelected
        ? new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#e8e8e8"))
        : new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#a0a5b5"));

    public FontWeight FontWeight => _isSelected ? FontWeights.Bold : FontWeights.Normal;

    public System.Windows.Media.Brush BadgeBackgroundBrush => _isSelected
        ? new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#e94560"))
        : new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#2a2a4e"));

    public System.Windows.Media.Brush BadgeForegroundBrush => _isSelected
        ? System.Windows.Media.Brushes.White
        : new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#8e9bb0"));

    public event PropertyChangedEventHandler? PropertyChanged;
}
