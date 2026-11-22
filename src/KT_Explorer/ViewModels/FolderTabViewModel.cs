using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using KT_Explorer.Models;

namespace KT_Explorer.ViewModels;

/// <summary>1つのタブ(1つのフォルダ表示と、その戻る/進む履歴)。</summary>
public partial class FolderTabViewModel : TabBase
{
    private readonly List<string> _back = new();
    private readonly List<string> _forward = new();
    private int _loadVersion;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ToolTipText))]
    private string _path = "";
    [ObservableProperty] private bool _canGoBack;
    [ObservableProperty] private bool _canGoForward;
    [ObservableProperty] private bool _canGoUp;
    [ObservableProperty] private string? _error;
    [ObservableProperty] private bool _isActivePane;

    public ObservableCollection<FileEntry> Items { get; } = new();

    public override string ToolTipText => Path;

    /// <summary>表示フォルダが変わったとき(ツリー連動用)。</summary>
    public event Action<FolderTabViewModel>? PathChanged;

    /// <summary>ペイン内でユーザーが操作を始めた(ペインタブの操作対象切替用)。</summary>
    public event Action<FolderTabViewModel>? Activated;

    public void RequestActivate() => Activated?.Invoke(this);

    /// <summary>項目を開く。フォルダなら移動、ファイルなら関連付けで起動。</summary>
    public void Open(FileEntry entry)
    {
        if (entry.IsDirectory)
        {
            NavigateTo(entry.FullPath);
            return;
        }
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(entry.FullPath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(ex.Message, "KT_Explorer");
        }
    }

    public FolderTabViewModel(string path) => NavigateTo(path, recordHistory: false);

    public void NavigateTo(string path, bool recordHistory = true)
    {
        string normalized = Normalize(path);
        if (string.Equals(normalized, Path, StringComparison.OrdinalIgnoreCase)) { Reload(); return; }

        if (recordHistory && !string.IsNullOrEmpty(Path))
        {
            _back.Add(Path);
            _forward.Clear();
        }
        SetPath(normalized);
    }

    public void GoBack()
    {
        if (_back.Count == 0) return;
        _forward.Add(Path);
        string target = _back[^1];
        _back.RemoveAt(_back.Count - 1);
        SetPath(target);
    }

    public void GoForward()
    {
        if (_forward.Count == 0) return;
        _back.Add(Path);
        string target = _forward[^1];
        _forward.RemoveAt(_forward.Count - 1);
        SetPath(target);
    }

    public void GoUp()
    {
        var parent = Directory.GetParent(Path);
        if (parent != null) NavigateTo(parent.FullName);
    }

    public void Reload() => _ = LoadAsync(Path);

    private void SetPath(string path)
    {
        Path = path;
        string leaf = System.IO.Path.GetFileName(path.TrimEnd('\\', '/'));
        Title = string.IsNullOrEmpty(leaf) ? path : leaf;
        CanGoBack = _back.Count > 0;
        CanGoForward = _forward.Count > 0;
        CanGoUp = Directory.GetParent(path) != null;
        PathChanged?.Invoke(this);
        _ = LoadAsync(path);
    }

    private async Task LoadAsync(string path)
    {
        int version = ++_loadVersion;
        List<FileEntry> entries;
        string? error = null;
        try
        {
            entries = await Task.Run(() => Read(path));
        }
        catch (Exception ex)
        {
            entries = new List<FileEntry>();
            error = ex.Message;
        }
        if (version != _loadVersion) return; // 古い読み込み結果は捨てる

        Items.Clear();
        foreach (var e in entries) Items.Add(e);
        Error = error;
    }

    private static List<FileEntry> Read(string path)
    {
        var dir = new DirectoryInfo(path);
        var list = new List<FileEntry>();
        foreach (var info in dir.EnumerateFileSystemInfos())
        {
            if ((info.Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0) continue;
            list.Add(new FileEntry(info));
        }
        list.Sort((a, b) =>
        {
            if (a.IsDirectory != b.IsDirectory) return a.IsDirectory ? -1 : 1;
            return string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase);
        });
        return list;
    }

    public static string Normalize(string path)
    {
        string full = System.IO.Path.GetFullPath(path.Trim().Trim('"'));
        string root = System.IO.Path.GetPathRoot(full) ?? "";
        return string.Equals(full, root, StringComparison.OrdinalIgnoreCase) ? root : full.TrimEnd('\\');
    }
}
