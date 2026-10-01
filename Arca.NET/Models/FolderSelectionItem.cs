using System.ComponentModel;

namespace Arca.NET.Models;

public class FolderSelectionItem : INotifyPropertyChanged
{
    private bool _isSelected;

    public string FolderName { get; set; } = string.Empty;
    public string DisplayName => string.IsNullOrWhiteSpace(FolderName) ? "(Sin carpeta / Raíz)" : FolderName;
    public int SecretCount { get; set; }

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
