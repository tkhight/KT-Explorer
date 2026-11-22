using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KT_Explorer.Models;
using KT_Explorer.Services;

namespace KT_Explorer.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private bool _syncingTree;
    private FolderTabViewModel? _observed;

    [ObservableProperty] private FolderTabViewModel? _selectedTab;
    [ObservableProperty] private bool _syncTreeWithTab = true;

    public ObservableCollection<FolderTabViewModel> Tabs { get; } = new();
    public ObservableCollection<TreeNodeViewModel> TreeRoots { get; } = new();

    public MainViewModel(AppState state)
    {
        SyncTreeWithTab = state.SyncTreeWithTab;
        BuildTreeRoots();

        foreach (var p in state.TabPaths.Where(Directory.Exists)) AddTab(p);
        if (Tabs.Count == 0)
            AddTab(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        SelectedTab = Tabs[Math.Clamp(state.SelectedIndex, 0, Tabs.Count - 1)];
    }

    private void BuildTreeRoots()
    {
        void AddRoot(string name, string path)
        {
            if (Directory.Exists(path)) TreeRoots.Add(new TreeNodeViewModel(name, path, OnTreeNodeSelected));
        }
        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        AddRoot("デスクトップ", Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
        AddRoot("ダウンロード", Path.Combine(profile, "Downloads"));
        AddRoot("ドキュメント", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
        foreach (var d in DriveInfo.GetDrives().Where(d => d.IsReady))
            AddRoot(d.Name.TrimEnd('\\'), d.RootDirectory.FullName);
    }

    private void OnTreeNodeSelected(TreeNodeViewModel node)
    {
        if (_syncingTree || SelectedTab == null) return;
        SelectedTab.NavigateTo(node.FullPath);
    }

    partial void OnSelectedTabChanged(FolderTabViewModel? value)
    {
        if (_observed != null) _observed.PathChanged -= OnActivePathChanged;
        _observed = value;
        if (value != null)
        {
            value.PathChanged += OnActivePathChanged;
            SyncTreeTo(value.Path);
        }
    }

    private void OnActivePathChanged(FolderTabViewModel tab) => SyncTreeTo(tab.Path);

    /// <summary>アクティブタブのフォルダへツリーの選択位置を合わせる(オプション)。</summary>
    private void SyncTreeTo(string path)
    {
        if (!SyncTreeWithTab) return;
        _syncingTree = true;
        try
        {
            TreeNodeViewModel? best = null;
            string bestRoot = "";
            foreach (var root in TreeRoots)
            {
                string rp = root.FullPath.TrimEnd('\\');
                bool match = path.Equals(rp, StringComparison.OrdinalIgnoreCase) ||
                             path.StartsWith(rp + "\\", StringComparison.OrdinalIgnoreCase);
                if (match && rp.Length > bestRoot.Length) { best = root; bestRoot = rp; }
            }
            var node = best?.ExpandTo(path);
            if (node != null) node.IsSelected = true;
        }
        finally
        {
            _syncingTree = false;
        }
    }

    private void AddTab(string path, bool select = false)
    {
        var tab = new FolderTabViewModel(path);
        Tabs.Add(tab);
        if (select) SelectedTab = tab;
    }

    [RelayCommand]
    private void NewTab()
    {
        AddTab(SelectedTab?.Path ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), select: true);
    }

    /// <summary>指定フォルダを新しいタブで開く(ツリー右クリック「タブ追加」)。</summary>
    public void OpenInNewTab(string path)
    {
        if (Directory.Exists(path)) AddTab(path, select: true);
    }

    [RelayCommand]
    private void CloseTab(FolderTabViewModel? tab)
    {
        tab ??= SelectedTab;
        if (tab == null || Tabs.Count <= 1) return; // 最後の1枚は閉じない
        int index = Tabs.IndexOf(tab);
        Tabs.Remove(tab);
        if (SelectedTab == tab || SelectedTab == null)
            SelectedTab = Tabs[Math.Min(index, Tabs.Count - 1)];
    }

    [RelayCommand] private void Back() => SelectedTab?.GoBack();
    [RelayCommand] private void Forward() => SelectedTab?.GoForward();
    [RelayCommand] private void Up() => SelectedTab?.GoUp();
    [RelayCommand] private void Refresh() => SelectedTab?.Reload();

    /// <summary>パス入力欄での Enter。存在するフォルダなら移動する。</summary>
    public bool NavigateToText(string text)
    {
        try
        {
            string p = FolderTabViewModel.Normalize(text);
            if (!Directory.Exists(p)) return false;
            SelectedTab?.NavigateTo(p);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public void Open(FileEntry entry)
    {
        if (entry.IsDirectory)
        {
            SelectedTab?.NavigateTo(entry.FullPath);
            return;
        }
        try
        {
            Process.Start(new ProcessStartInfo(entry.FullPath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(ex.Message, "KT_Explorer");
        }
    }

    public AppState ToState() => new()
    {
        TabPaths = Tabs.Select(t => t.Path).ToList(),
        SelectedIndex = SelectedTab == null ? 0 : Tabs.IndexOf(SelectedTab),
        SyncTreeWithTab = SyncTreeWithTab,
    };
}
