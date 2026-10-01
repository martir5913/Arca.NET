using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;

namespace Arca.NET.Models;

public class ApiKeyTreeNode : INotifyPropertyChanged
{
    private bool? _isSelected = false;
    private bool _isExpanded = true;
    private bool _isMatchingFilter = true;

    public string Name { get; set; } = string.Empty;
    public string DisplayText { get; set; } = string.Empty;
    public string Icon { get; set; } = "📁";
    public bool IsFolder { get; set; }
    public string? FullKey { get; set; } // null for folders, "Folder:Key" or "Key" for secrets
    public string? FolderName { get; set; }

    public ApiKeyTreeNode? Parent { get; set; }
    public ObservableCollection<ApiKeyTreeNode> Children { get; set; } = new();

    public bool? IsSelected
    {
        get => _isSelected;
        set => SetIsSelected(value, updateChildren: true, updateParent: true);
    }

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded != value)
            {
                _isExpanded = value;
                OnPropertyChanged(nameof(IsExpanded));
            }
        }
    }

    public bool IsMatchingFilter
    {
        get => _isMatchingFilter;
        set
        {
            if (_isMatchingFilter != value)
            {
                _isMatchingFilter = value;
                OnPropertyChanged(nameof(IsMatchingFilter));
                OnPropertyChanged(nameof(Visibility));
            }
        }
    }

    public Visibility Visibility => _isMatchingFilter ? Visibility.Visible : Visibility.Collapsed;

    public void SetIsSelected(bool? value, bool updateChildren, bool updateParent)
    {
        if (_isSelected != value)
        {
            _isSelected = value;
            OnPropertyChanged(nameof(IsSelected));

            if (updateChildren && value.HasValue && Children.Count > 0)
            {
                foreach (var child in Children)
                {
                    child.SetIsSelected(value, updateChildren: true, updateParent: false);
                }
            }

            if (updateParent && Parent != null)
            {
                Parent.VerifyCheckState();
            }
        }
    }

    public void VerifyCheckState()
    {
        bool? state = null;
        for (int i = 0; i < Children.Count; ++i)
        {
            bool? current = Children[i].IsSelected;
            if (i == 0)
            {
                state = current;
            }
            else if (state != current)
            {
                state = null; // Tri-state: mixed selection
                break;
            }
        }
        SetIsSelected(state, updateChildren: false, updateParent: true);
    }

    /// <summary>
    /// Filters tree based on search text: matches folder name or child secret key
    /// </summary>
    public bool ApplyFilter(string filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
        {
            IsMatchingFilter = true;
            foreach (var child in Children)
            {
                child.ApplyFilter(filter);
            }
            return true;
        }

        if (IsFolder)
        {
            bool folderMatches = Name.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                                 DisplayText.Contains(filter, StringComparison.OrdinalIgnoreCase);

            bool anyChildMatches = false;
            foreach (var child in Children)
            {
                if (child.ApplyFilter(filter))
                {
                    anyChildMatches = true;
                }
            }

            // If folder name matches, show all children as matching
            if (folderMatches)
            {
                foreach (var child in Children)
                {
                    child.IsMatchingFilter = true;
                }
            }

            IsMatchingFilter = folderMatches || anyChildMatches;
            if (IsMatchingFilter)
            {
                IsExpanded = true;
            }
            return IsMatchingFilter;
        }
        else
        {
            bool matches = (FullKey != null && FullKey.Contains(filter, StringComparison.OrdinalIgnoreCase)) ||
                           Name.Contains(filter, StringComparison.OrdinalIgnoreCase);
            IsMatchingFilter = matches;
            return matches;
        }
    }

    /// <summary>
    /// Recursively collects all selected full keys from this node and its children
    /// </summary>
    public void CollectSelectedKeys(List<string> selectedKeys, List<string> selectedFolderPrefixes)
    {
        if (IsFolder)
        {
            if (IsSelected == true && !string.IsNullOrEmpty(FolderName))
            {
                selectedFolderPrefixes.Add($"{FolderName}:*");
            }

            foreach (var child in Children)
            {
                child.CollectSelectedKeys(selectedKeys, selectedFolderPrefixes);
            }
        }
        else
        {
            if (IsSelected == true && !string.IsNullOrEmpty(FullKey))
            {
                selectedKeys.Add(FullKey);
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged(string propName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));
}
