using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;

namespace KT_Explorer.ViewModels;

/// <summary>フォルダツリーの1ノード。子は展開時に遅延読み込みする。</summary>
public partial class TreeNodeViewModel : ObservableObject
{
    private readonly Action<TreeNodeViewModel> _onSelected;
    private bool _loaded;

    [ObservableProperty] private bool _isExpanded;
    [ObservableProperty] private bool _isSelected;

    public TreeNodeViewModel(string name, string fullPath, Action<TreeNodeViewModel> onSelected)
    {
        Name = name;
        FullPath = fullPath;
        _onSelected = onSelected;
        Children.Add(Placeholder(onSelected)); // 展開矢印を出すためのダミー
    }

    private TreeNodeViewModel(Action<TreeNodeViewModel> onSelected)
    {
        Name = "…";
        FullPath = "";
        _onSelected = onSelected;
        _loaded = true;
        IsPlaceholder = true;
    }

    private static TreeNodeViewModel Placeholder(Action<TreeNodeViewModel> cb) => new(cb);

    public string Name { get; }
    public string FullPath { get; }
    public bool IsPlaceholder { get; }
    public ObservableCollection<TreeNodeViewModel> Children { get; } = new();

    partial void OnIsExpandedChanged(bool value)
    {
        if (value) EnsureChildren();
    }

    partial void OnIsSelectedChanged(bool value)
    {
        if (value && !IsPlaceholder) _onSelected(this);
    }

    public void EnsureChildren()
    {
        if (_loaded) return;
        _loaded = true;
        Children.Clear();
        try
        {
            var dirs = new DirectoryInfo(FullPath)
                .EnumerateDirectories()
                .Where(d => (d.Attributes & (FileAttributes.Hidden | FileAttributes.System)) == 0)
                .OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase);
            foreach (var d in dirs)
                Children.Add(new TreeNodeViewModel(d.Name, d.FullName, _onSelected));
        }
        catch
        {
            // アクセス不可フォルダは子なしとして扱う
        }
    }

    /// <summary>target までのノードを展開し、最後のノードを返す。</summary>
    public TreeNodeViewModel? ExpandTo(string target)
    {
        if (string.Equals(FullPath.TrimEnd('\\'), target.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) return this;
        IsExpanded = true;
        foreach (var child in Children)
        {
            if (child.IsPlaceholder) continue;
            string prefix = child.FullPath.TrimEnd('\\') + "\\";
            if (target.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(child.FullPath.TrimEnd('\\'), target.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                return child.ExpandTo(target);
        }
        return null;
    }
}
