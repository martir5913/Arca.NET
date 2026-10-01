using Arca.Core.Entities;
using System.ComponentModel;
using System.Windows;

namespace Arca.NET.Models;

public class SecretDisplayItem : INotifyPropertyChanged
{
    private bool _isRevealed;

    public SecretEntry Secret { get; }

    public Guid Id => Secret.Id;
    public string Key => Secret.Key;
    public string Value => Secret.Value;
    public string? Folder => Secret.Folder;
    public string? Description => Secret.Description;
    public string? Environment => Secret.Environment;
    public List<string>? Tags => Secret.Tags;
    public DateTime CreatedAt => Secret.CreatedAt;
    public DateTime? ModifiedAt => Secret.ModifiedAt;
    public string FullKey => Secret.FullKey;

    public bool IsRevealed
    {
        get => _isRevealed;
        set
        {
            if (_isRevealed != value)
            {
                _isRevealed = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsRevealed)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayValue)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RevealIcon)));
            }
        }
    }

    public string DisplayValue => _isRevealed ? Value : "••••••••••••••••";
    public string RevealIcon => _isRevealed ? "🙈" : "👁️";

    public Visibility FolderVisibility => !string.IsNullOrWhiteSpace(Folder) ? Visibility.Visible : Visibility.Collapsed;
    public Visibility EnvironmentVisibility => !string.IsNullOrWhiteSpace(Environment) ? Visibility.Visible : Visibility.Collapsed;
    public Visibility DescriptionVisibility => !string.IsNullOrWhiteSpace(Description) ? Visibility.Visible : Visibility.Collapsed;

    public SecretDisplayItem(SecretEntry secret)
    {
        Secret = secret;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
