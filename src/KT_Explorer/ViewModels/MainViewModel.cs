using System.Collections.ObjectModel;
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
    private TabBase? _selectedTab;
    private TabGroupViewModel _selectedGroup = null!;

    [ObservableProperty] private bool _syncTreeWithTab = true;
    [ObservableProperty] private bool _enableGestures = true;
    [ObservableProperty] private bool _captureExplorer = true;
    [ObservableProperty] private bool _closeToTray = true;

    /// <summary>ツールバー/パス欄/ツリー連動の対象。ペインタブでは操作中のペイン。</summary>
    [ObservableProperty] private FolderTabViewModel? _currentFolder;

    public ObservableCollection<TabGroupViewModel> Groups { get; } = new();
    public ObservableCollection<TreeNodeViewModel> TreeRoots { get; } = new();
    public ObservableCollection<Favorite> Favorites { get; } = new();

    public MainViewModel(AppState state)
    {
        SyncTreeWithTab = state.SyncTreeWithTab;
        EnableGestures = state.EnableGestures;
        CaptureExplorer = state.CaptureExplorer;
        CloseToTray = state.CloseToTray;
        foreach (var f in state.Favorites.Where(f => !string.IsNullOrWhiteSpace(f.Path)))
            Favorites.Add(new Favorite(string.IsNullOrWhiteSpace(f.Name) ? f.Path : f.Name, f.Path));
        BuildTreeRoots();
        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        // 旧形式(グループ無し)の状態は、デフォルトグループへ移行する
        var saved = state.Groups.Count > 0
            ? state.Groups
            : new List<GroupState> { new() { Name = "デフォルト", IsDefault = true, TabPaths = state.TabPaths, SelectedIndex = state.SelectedIndex } };

        for (int i = 0; i < saved.Count; i++)
        {
            var gs = saved[i];
            var group = new TabGroupViewModel(string.IsNullOrWhiteSpace(gs.Name) ? "グループ" : gs.Name, isDefault: i == 0);
            foreach (var p in gs.TabPaths.Where(Directory.Exists)) group.Tabs.Add(new FolderTabViewModel(p));
            if (group.Tabs.Count > 0)
                group.LastSelected = group.Tabs[Math.Clamp(gs.SelectedIndex, 0, group.Tabs.Count - 1)];
            Groups.Add(group);
        }
        if (Groups.Sum(g => g.Tabs.Count) == 0) Groups[0].Tabs.Add(new FolderTabViewModel(profile));

        SelectGroup(Groups[Math.Clamp(state.SelectedGroupIndex, 0, Groups.Count - 1)]);
    }

    // ───────── 選択状態(ListBox が null を書き戻しても無視する) ─────────

    public TabGroupViewModel SelectedGroup
    {
        get => _selectedGroup;
        set { if (value != null && value != _selectedGroup) SelectGroup(value); }
    }

    public TabBase? SelectedTab
    {
        get => _selectedTab;
        set { if (value != null && value != _selectedTab) Select(value); }
    }

    private void SelectGroup(TabGroupViewModel group)
    {
        if (_selectedGroup != null) _selectedGroup.LastSelected = _selectedTab;
        _selectedGroup = group;
        OnPropertyChanged(nameof(SelectedGroup));
        var target = group.LastSelected != null && group.Tabs.Contains(group.LastSelected)
            ? group.LastSelected
            : group.Tabs.FirstOrDefault();
        Select(target);
    }

    private void Select(TabBase? tab)
    {
        _selectedTab = tab;
        _selectedGroup.LastSelected = tab;
        OnPropertyChanged(nameof(SelectedTab));
        UpdateCurrentFolder();
    }

    private void UpdateCurrentFolder()
    {
        var folder = _selectedTab switch
        {
            FolderTabViewModel f => f,
            PaneTabViewModel p => p.ActivePane,
            _ => null,
        };
        if (_observed != null) _observed.PathChanged -= OnActivePathChanged;
        _observed = folder;
        CurrentFolder = folder;
        if (folder != null)
        {
            folder.PathChanged += OnActivePathChanged;
            SyncTreeTo(folder.Path);
        }
    }

    private void OnActivePathChanged(FolderTabViewModel tab) => SyncTreeTo(tab.Path);

    // ───────── ツリー ─────────

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
        if (_syncingTree) return;
        CurrentFolder?.NavigateTo(node.FullPath);
    }

    /// <summary>アクティブなフォルダへツリーの選択位置を合わせる(オプション)。</summary>
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

    // ───────── タブ ─────────

    private string DefaultPath => CurrentFolder?.Path ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    private FolderTabViewModel AddTab(string path, bool select = false)
    {
        var tab = new FolderTabViewModel(path);
        _selectedGroup.Tabs.Add(tab);
        if (select) Select(tab);
        return tab;
    }

    [RelayCommand]
    private void NewTab() => AddTab(DefaultPath, select: true);

    /// <summary>指定フォルダを新しいタブで開く(ツリー右クリック「タブ追加」)。</summary>
    public void OpenInNewTab(string path)
    {
        if (Directory.Exists(path)) AddTab(path, select: true);
    }

    [RelayCommand]
    private void CloseTab(TabBase? tab)
    {
        tab ??= _selectedTab;
        if (tab == null) return;
        var group = Groups.FirstOrDefault(g => g.Tabs.Contains(tab));
        if (group == null || Groups.Sum(g => g.Tabs.Count) <= 1) return; // 最後の1枚は閉じない

        int index = group.Tabs.IndexOf(tab);
        group.Tabs.Remove(tab);
        tab.Dispose();
        if (ReferenceEquals(tab, _selectedTab) && group == _selectedGroup)
            Select(group.Tabs.Count > 0 ? group.Tabs[Math.Min(index, group.Tabs.Count - 1)] : null);
    }

    public void CycleTab(int direction)
    {
        var tabs = _selectedGroup.Tabs;
        if (tabs.Count < 2 || _selectedTab == null) return;
        int i = tabs.IndexOf(_selectedTab);
        Select(tabs[(i + direction + tabs.Count) % tabs.Count]);
    }

    // ───────── タブグループ ─────────

    public void AddGroup(string name)
    {
        var group = new TabGroupViewModel(name.Trim());
        group.Tabs.Add(new FolderTabViewModel(DefaultPath));
        Groups.Add(group);
        SelectGroup(group);
    }

    public void RenameGroup(TabGroupViewModel group, string name)
    {
        if (!string.IsNullOrWhiteSpace(name)) group.Name = name.Trim();
    }

    /// <summary>グループを削除する(中のタブも閉じる)。デフォルトグループは削除できない。</summary>
    public bool DeleteGroup(TabGroupViewModel group)
    {
        if (group.IsDefault || !Groups.Contains(group)) return false;
        bool wasSelected = group == _selectedGroup;
        Groups.Remove(group);
        foreach (var tab in group.Tabs) tab.Dispose();
        if (wasSelected) SelectGroup(Groups[0]);
        return true;
    }

    public void CycleGroup(int direction)
    {
        if (Groups.Count < 2) return;
        int i = Groups.IndexOf(_selectedGroup);
        SelectGroup(Groups[(i + direction + Groups.Count) % Groups.Count]);
    }

    /// <summary>タブを別のグループへ移動する。表示中のグループは変えない。</summary>
    public void MoveTabToGroup(TabBase tab, TabGroupViewModel target)
    {
        var source = Groups.FirstOrDefault(g => g.Tabs.Contains(tab));
        if (source == null || source == target) return;

        int index = source.Tabs.IndexOf(tab);
        source.Tabs.Remove(tab);
        target.Tabs.Add(tab);
        target.LastSelected ??= tab;

        if (source == _selectedGroup && ReferenceEquals(tab, _selectedTab))
            Select(source.Tabs.Count > 0 ? source.Tabs[Math.Min(index, source.Tabs.Count - 1)] : null);
    }

    // ───────── ペイン ─────────

    /// <summary>2/3ペイン。アクティブなタブのフォルダを右側に追加する(ペインタブ未作成なら作成)。</summary>
    [RelayCommand]
    private void OpenPanes(string countText)
    {
        if (!int.TryParse(countText, out int count) || CurrentFolder == null) return;
        string path = CurrentFolder.Path;
        if (_selectedTab is PaneTabViewModel existing)
        {
            while (existing.Panes.Count < count && existing.AddPane(path)) { }
            return;
        }
        CreatePaneTab(Enumerable.Repeat(path, count));
    }

    /// <summary>指定フォルダを右ペインとして追加する(既存タブ選択 / フォルダ指定モード)。</summary>
    public void AddPane(string path)
    {
        if (!Directory.Exists(path)) return;
        if (_selectedTab is PaneTabViewModel existing) existing.AddPane(path);
        else if (_selectedTab is FolderTabViewModel f) CreatePaneTab(new[] { f.Path, path });
    }

    /// <summary>既存タブから選ぶための候補(全グループのフォルダタブ)。</summary>
    public IEnumerable<(TabGroupViewModel Group, FolderTabViewModel Tab)> AllFolderTabs() =>
        Groups.SelectMany(g => g.Tabs.OfType<FolderTabViewModel>().Select(t => (g, t)));

    private void CreatePaneTab(IEnumerable<string> paths)
    {
        var pane = new PaneTabViewModel(paths);
        pane.ActivePaneChanged += UpdateCurrentFolder;
        pane.RequestClose += ClosePaneTab;

        // カレントタブの右隣に作る(既存タブの展開ではなく、独立した特殊タブ)
        int index = _selectedTab == null ? _selectedGroup.Tabs.Count : _selectedGroup.Tabs.IndexOf(_selectedTab) + 1;
        _selectedGroup.Tabs.Insert(Math.Min(index, _selectedGroup.Tabs.Count), pane);
        Select(pane);
    }

    /// <summary>ペインが1つになったペインタブを閉じ、元のタブへ戻る。</summary>
    private void ClosePaneTab(PaneTabViewModel pane)
    {
        var group = Groups.FirstOrDefault(g => g.Tabs.Contains(pane));
        if (group == null) return;
        int index = group.Tabs.IndexOf(pane);
        group.Tabs.Remove(pane);
        pane.Dispose();

        if (Groups.Sum(g => g.Tabs.Count) == 0)
            group.Tabs.Add(new FolderTabViewModel(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)));

        if (ReferenceEquals(pane, _selectedTab) && group == _selectedGroup)
            Select(group.Tabs.Count > 0 ? group.Tabs[Math.Max(index - 1, 0)] : null);
    }

    // ───────── お気に入り ─────────

    /// <summary>フォルダをお気に入りに追加する。すでにあれば何もしない。</summary>
    public void AddFavorite(string path)
    {
        if (!Directory.Exists(path)) return;
        string p = FolderTabViewModel.Normalize(path);
        if (Favorites.Any(f => string.Equals(f.Path, p, StringComparison.OrdinalIgnoreCase))) return;
        string leaf = Path.GetFileName(p.TrimEnd('\\', '/'));
        Favorites.Add(new Favorite(string.IsNullOrEmpty(leaf) ? p : leaf, p));
    }

    [RelayCommand]
    private void AddCurrentToFavorites()
    {
        if (CurrentFolder != null) AddFavorite(CurrentFolder.Path);
    }

    public void RemoveFavorite(Favorite fav) => Favorites.Remove(fav);

    /// <summary>お気に入りを現在のフォルダ(操作中のペイン)で開く。存在しなければ false。</summary>
    public bool OpenFavorite(Favorite fav, bool inNewTab)
    {
        if (!Directory.Exists(fav.Path)) return false;
        if (inNewTab || CurrentFolder == null) AddTab(fav.Path, select: true);
        else CurrentFolder.NavigateTo(fav.Path);
        return true;
    }

    // ───────── Explorer 取り込み ─────────

    /// <summary>外部で開かれたフォルダをデフォルトグループのタブとして取り込み、そのタブを表示する。</summary>
    public void ImportFolder(string path)
    {
        string p = FolderTabViewModel.Normalize(path);
        var def = Groups[0];
        var tab = def.Tabs.OfType<FolderTabViewModel>()
                      .FirstOrDefault(t => string.Equals(t.Path, p, StringComparison.OrdinalIgnoreCase));
        if (tab == null)
        {
            tab = new FolderTabViewModel(p);
            def.Tabs.Add(tab);
        }
        if (_selectedGroup != def) SelectGroup(def);
        Select(tab);
    }

    // ───────── ナビゲーション ─────────

    [RelayCommand] private void Back() => CurrentFolder?.GoBack();
    [RelayCommand] private void Forward() => CurrentFolder?.GoForward();
    [RelayCommand] private void Up() => CurrentFolder?.GoUp();
    [RelayCommand] private void Refresh() => CurrentFolder?.Reload();

    /// <summary>パス入力欄での Enter。存在するフォルダなら移動する。</summary>
    public bool NavigateToText(string text)
    {
        try
        {
            string p = FolderTabViewModel.Normalize(text);
            if (!Directory.Exists(p)) return false;
            CurrentFolder?.NavigateTo(p);
            return true;
        }
        catch
        {
            return false;
        }
    }

    // ───────── 保存 ─────────

    /// <summary>永続化する状態。ペインタブは一時的なものなので保存しない。</summary>
    public AppState ToState()
    {
        var state = new AppState
        {
            SyncTreeWithTab = SyncTreeWithTab,
            EnableGestures = EnableGestures,
            CaptureExplorer = CaptureExplorer,
            CloseToTray = CloseToTray,
            SelectedGroupIndex = Groups.IndexOf(_selectedGroup),
        };
        foreach (var f in Favorites) state.Favorites.Add(new FavoriteState { Name = f.Name, Path = f.Path });
        foreach (var g in Groups)
        {
            var selected = ReferenceEquals(g, _selectedGroup) ? _selectedTab : g.LastSelected;
            var folderTabs = new List<string>();
            int selectedIndex = 0;
            foreach (var t in g.Tabs)
            {
                if (t is FolderTabViewModel f) folderTabs.Add(f.Path);
                if (ReferenceEquals(t, selected)) selectedIndex = Math.Max(folderTabs.Count - 1, 0);
            }
            state.Groups.Add(new GroupState { Name = g.Name, IsDefault = g.IsDefault, TabPaths = folderTabs, SelectedIndex = selectedIndex });
        }
        return state;
    }
}
