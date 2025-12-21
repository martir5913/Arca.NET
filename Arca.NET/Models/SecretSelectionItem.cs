using System.ComponentModel;

namespace Arca.NET.Models;

public class SecretSelectionItem : INotifyPropertyChanged
{
    private bool _isSelected;

    public string Key { get; set; } = string.Empty;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected != value)
            {
                _isSelected = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
