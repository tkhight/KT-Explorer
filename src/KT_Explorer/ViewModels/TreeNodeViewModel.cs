using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using KT_Explorer.Services;

namespace KT_Explorer.ViewModels;

/// <summary>フォルダツリーの1ノード。子は展開時に遅延読み込みする。</summary>
public partial class TreeNodeViewModel : ObservableObject
{
    private readonly Action<TreeNodeViewModel> _onSelected;
    private bool _loaded;

    [ObservableProperty] private bool _isExpanded;
    [ObservableProperty] private bool _isSelected;

    /// <summary>表示名。名前を変えたあと、ノードを作り直さずに更新できるよう、通知つきにしている。</summary>
    [ObservableProperty] private string _name = "";

    /// <summary>名前の直接編集中か(ツリーの項目が入力欄に切り替わる)。</summary>
    [ObservableProperty] private bool _isEditing;

    /// <summary>直接編集中の入力内容。</summary>
    [ObservableProperty] private string _editName = "";

    public TreeNodeViewModel(string name, string fullPath, Action<TreeNodeViewModel> onSelected, TreeNodeViewModel? parent = null)
    {
        Parent = parent;
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

    public string FullPath { get; }
    public bool IsPlaceholder { get; }

    /// <summary>親のノード(ルートは null)。ツリーを仮想化しているため、選択したノードまでスクロールするときに、親をたどって、順に表示させる。</summary>
    public TreeNodeViewModel? Parent { get; }

    /// <summary>フォルダのアイコン(通常は共通のフォルダのアイコン。ドライブ・デスクトップなどは固有のもの)。ダミーの子には無い。</summary>
    public ImageSource? Icon => IsPlaceholder ? null : ShellIconService.GetIcon(FullPath, isDirectory: true);

    private OverlayLoader? _overlayLoader;
    private OverlayLoader Overlay => _overlayLoader ??= new OverlayLoader(() => FullPath, () => OnPropertyChanged(nameof(OverlayIcon)));

    /// <summary>アイコンに重ねるオーバーレイ(OneDrive・SVN などの状態)。表示されたとき、バックグラウンドで調べる。</summary>
    public ImageSource? OverlayIcon => IsPlaceholder ? null : Overlay.Small;

    /// <summary>オーバーレイを、いったん消して調べ直す(「オーバーレイを表示」の切り替え。展開済みの子も含む)。</summary>
    public void ResetOverlay()
    {
        _overlayLoader?.Reset();
        foreach (var child in Children) child.ResetOverlay();
    }
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
                Children.Add(new TreeNodeViewModel(d.Name, d.FullName, _onSelected, this));
        }
        catch
        {
            // アクセス不可フォルダは子なしとして扱う
        }
    }

    /// <summary>
    /// 読み込み済みの子を、実際のフォルダの状態に合わせて更新する(作成・削除・名前の変更のあと)。
    /// 残る子はそのまま使うので、展開状態は保たれる。
    /// </summary>
    public void RefreshChildren()
    {
        if (!_loaded || IsPlaceholder) return;

        List<DirectoryInfo> dirs;
        try
        {
            dirs = new DirectoryInfo(FullPath)
                .EnumerateDirectories()
                .Where(d => (d.Attributes & (FileAttributes.Hidden | FileAttributes.System)) == 0)
                .OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }
        catch
        {
            dirs = new();
        }

        // 無くなった子(大文字小文字だけ変わった名前も、表示名を作り直すため入れ替える)を取り除く
        for (int i = Children.Count - 1; i >= 0; i--)
        {
            if (Children[i].IsPlaceholder || !dirs.Any(d => string.Equals(d.FullName, Children[i].FullPath, StringComparison.Ordinal)))
                Children.RemoveAt(i);
        }
        // 残りは dirs の部分列(同じ並び順)なので、足りない子を位置に挿入していく
        for (int i = 0; i < dirs.Count; i++)
        {
            if (i < Children.Count && string.Equals(Children[i].FullPath, dirs[i].FullName, StringComparison.Ordinal)) continue;
            Children.Insert(i, new TreeNodeViewModel(dirs[i].Name, dirs[i].FullName, _onSelected, this));
        }
        foreach (var child in Children)
        {
            child.RefreshChildren();
            child._overlayLoader?.Refresh(); // 状態が変わったかもしれない
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
