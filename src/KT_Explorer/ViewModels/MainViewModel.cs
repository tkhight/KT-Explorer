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

    /// <summary>プレビューウィンドウを表示する。</summary>
    [ObservableProperty] private bool _showPreview;

    /// <summary>プレビューウィンドウの表示内容(操作中のペインで選択している項目)。</summary>
    public PreviewViewModel Preview { get; } = new();

    partial void OnShowPreviewChanged(bool value)
    {
        if (value) RefreshPreview(); else Preview.Clear();
    }

    [RelayCommand]
    private void TogglePreview() => ShowPreview = !ShowPreview;

    /// <summary>操作中の一覧の表示を切り替える(Ctrl+Shift+1 詳細 / 2 アイコン / 3 一覧)。</summary>
    [RelayCommand]
    private void SetViewMode(string? mode)
    {
        if (Enum.TryParse<ViewMode>(mode, out var m)) CurrentFolder?.SetViewMode(m);
    }

    private void RefreshPreview()
    {
        if (ShowPreview) Preview.Show(CurrentFolder?.Selection ?? Array.Empty<FileEntry>());
    }

    private void OnCurrentSelectionChanged() => RefreshPreview();

    /// <summary>一覧を開いたとき、フォルダの合計サイズを自動で計算する。</summary>
    [ObservableProperty] private bool _autoFolderSize;

    partial void OnAutoFolderSizeChanged(bool value)
    {
        FolderSizeSettings.Auto = value;
        if (value) CurrentFolder?.CalculateAllFolderSizes(); // オンにした時点で、表示中の一覧も計算する
    }

    /// <summary>ツールバー/パス欄/ツリー連動の対象。ペインタブでは操作中のペイン。</summary>
    [ObservableProperty] private FolderTabViewModel? _currentFolder;

    public ObservableCollection<TabGroupViewModel> Groups { get; } = new();
    public ObservableCollection<TreeNodeViewModel> TreeRoots { get; } = new();
    public ObservableCollection<Favorite> Favorites { get; } = new();

    // ───────── 元に戻す / やり直し(ファイル操作の履歴) ─────────

    public bool CanUndo => UndoManager.Instance.CanUndo;
    public bool CanRedo => UndoManager.Instance.CanRedo;
    public string UndoToolTip => UndoManager.Instance.UndoText is { } t ? $"元に戻す (Ctrl+Z): {t}" : "元に戻す (Ctrl+Z)";
    public string RedoToolTip => UndoManager.Instance.RedoText is { } t ? $"やり直し (Ctrl+Y): {t}" : "やり直し (Ctrl+Y)";

    /// <summary>直前のファイル操作を元に戻す。できなければ理由を返す。</summary>
    public string? Undo(IntPtr owner)
    {
        PauseWatching(null); // 元に戻す操作が、開いているフォルダの名前の変更・削除を含むことがあるため
        var changes = UndoManager.Instance.NextUndoPathChanges;
        var error = UndoManager.Instance.Undo(owner);
        FollowPathChanges(error, changes);
        return error;
    }

    /// <summary>元に戻した操作をやり直す。できなければ理由を返す。</summary>
    public string? Redo(IntPtr owner)
    {
        PauseWatching(null);
        var changes = UndoManager.Instance.NextRedoPathChanges;
        var error = UndoManager.Instance.Redo(owner);
        FollowPathChanges(error, changes);
        return error;
    }

    // ───────── フォルダの名前の変更・削除に伴うタブ・ツリーの追従 ─────────

    /// <summary>
    /// 元に戻す・やり直しのあと、名前や場所が変わったフォルダを表示しているタブ・ペインを、新しい名前・場所へ追従させる
    /// (元の名前へ戻す、など)。それ以外で無くなったフォルダは、近い親フォルダへ移す。
    /// </summary>
    private void FollowPathChanges(string? error, IReadOnlyList<(string From, string To)> changes)
    {
        if (error == null)
        {
            foreach (var (from, to) in changes)
                if (Directory.Exists(to)) OnFolderRenamed(from, to);
        }
        AfterFileChange();
    }

    private IEnumerable<FolderTabViewModel> AllFolders() =>
        Groups.SelectMany(g => g.Tabs).SelectMany(t => t switch
        {
            FolderTabViewModel f => new[] { f },
            PaneTabViewModel p => p.Panes.ToArray(),
            _ => Array.Empty<FolderTabViewModel>(),
        });

    private static bool IsSameOrInside(string path, string folder) =>
        path.TrimEnd('\\').Equals(folder, StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith(folder + "\\", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 指定フォルダ(とその中)を表示しているタブ・ペインの監視を止める。null なら、すべて止める。
    /// 監視中のフォルダは、そのフォルダも親も、名前の変更・削除ができない(アクセス拒否になる)ため、操作の前に呼ぶ。
    /// 操作のあとは <see cref="ResumeAllWatching"/>(または <see cref="AfterFileChange"/>)で再開する。
    /// </summary>
    public void PauseWatching(string? folderPath)
    {
        string? folder = folderPath?.TrimEnd('\\');
        foreach (var f in AllFolders().ToList())
            if (folder == null || IsSameOrInside(f.Path, folder)) f.PauseWatching();
    }

    public void ResumeAllWatching()
    {
        foreach (var f in AllFolders().ToList()) f.ResumeWatching();
    }

    /// <summary>
    /// フォルダの名前を変えたあとに呼ぶ。そのフォルダ(や、その中)を表示しているタブ・ペインと、
    /// お気に入りを、新しい名前に追従させる。
    /// </summary>
    public void OnFolderRenamed(string oldPath, string newPath)
    {
        string o = oldPath.TrimEnd('\\');
        string n = newPath.TrimEnd('\\');
        // 先にツリーを更新する(新しい名前のノードを作っておく)。そのあとでタブを移すと、ツリーの選択が新しいノードへ合う
        RefreshTree();
        foreach (var f in AllFolders().ToList())
            if (IsSameOrInside(f.Path, o)) f.NavigateTo(n + f.Path.TrimEnd('\\').Substring(o.Length), recordHistory: false);
        foreach (var fav in FavoriteTree.Folders(Favorites))
            if (IsSameOrInside(fav.Path, o)) fav.Path = n + fav.Path.TrimEnd('\\').Substring(o.Length);
    }

    /// <summary>
    /// フォルダの削除・元に戻す・やり直しのあとに呼ぶ。無くなったフォルダを表示しているタブ・ペインは、
    /// 残っている最も近い親フォルダへ移し、ツリーを実際の状態に合わせる。
    /// </summary>
    public void AfterFileChange()
    {
        RefreshTree();
        foreach (var f in AllFolders().ToList())
        {
            if (Directory.Exists(f.Path)) continue;
            string? p = f.Path;
            while (p != null && !Directory.Exists(p)) p = Directory.GetParent(p)?.FullName;
            if (p != null) f.NavigateTo(p, recordHistory: false);
        }
        ResumeAllWatching();
    }

    /// <summary>ツリーの読み込み済みの部分を、実際のフォルダの状態に合わせる。</summary>
    public void RefreshTree()
    {
        // 選択中のノードが無くなると、ツリーの選択が変わって、その選択がタブの移動を引き起こしてしまう
        // (名前を変えたフォルダを表示中のタブが、親フォルダへ移る)。更新の間は、ツリーの選択を無視する。
        bool previous = _syncingTree;
        _syncingTree = true;
        try
        {
            foreach (var root in TreeRoots) root.RefreshChildren();
        }
        finally
        {
            _syncingTree = previous;
        }
    }

    private void OnUndoChanged()
    {
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
        OnPropertyChanged(nameof(UndoToolTip));
        OnPropertyChanged(nameof(RedoToolTip));
    }

    public MainViewModel(AppState state)
    {
        UndoManager.Instance.Changed += OnUndoChanged;

        SyncTreeWithTab = state.SyncTreeWithTab;
        EnableGestures = state.EnableGestures;
        CaptureExplorer = state.CaptureExplorer;
        CloseToTray = state.CloseToTray;
        SortSettings.Instance.Load(state.SortColumn, state.SortDescending, state.FoldersFirst);
        SortSettings.Instance.DefaultViewMode = state.ViewMode;
        AutoFolderSize = state.AutoFolderSize;
        ShowPreview = state.ShowPreview;
        foreach (var fs in state.Favorites)
        {
            var fav = FavoriteFromState(fs);
            if (fav != null) Favorites.Add(fav);
        }
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
            for (int k = 0; k < gs.TabPaths.Count; k++)
            {
                if (!Directory.Exists(gs.TabPaths[k])) continue;
                var tab = new FolderTabViewModel(gs.TabPaths[k]);
                if (k < gs.TabSorts.Count)
                {
                    tab.SetSort(gs.TabSorts[k].Column, gs.TabSorts[k].Descending); // タブごとの並び順を復元
                    tab.SetViewMode(gs.TabSorts[k].ViewMode, remember: false);      // タブごとの表示(詳細・アイコン・一覧)を復元
                }
                group.Tabs.Add(tab);
            }
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
        if (_observed != null)
        {
            _observed.PathChanged -= OnActivePathChanged;
            _observed.SelectionChanged -= OnCurrentSelectionChanged;
        }
        _observed = folder;
        CurrentFolder = folder;
        if (folder != null)
        {
            folder.PathChanged += OnActivePathChanged;
            folder.SelectionChanged += OnCurrentSelectionChanged;
            SyncTreeTo(folder.Path);
        }
        RefreshPreview(); // 操作中の一覧(タブ・ペイン)が変わったので、そちらの選択を表示する
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

    // 閉じたフォルダタブの履歴(新しいものが末尾)。ペインタブは対象外
    private sealed record ClosedTab(string Path, TabGroupViewModel Group, int Index);
    private readonly List<ClosedTab> _closedTabs = new();
    private const int ClosedTabLimit = 20;

    public bool CanReopenClosedTab => _closedTabs.Count > 0;

    /// <summary>直前に閉じたタブを、元のグループの元の位置に開き直す(Ctrl+Shift+T)。フォルダが無くなっていれば、その前のものへ。</summary>
    [RelayCommand]
    private void ReopenClosedTab()
    {
        while (_closedTabs.Count > 0)
        {
            var closed = _closedTabs[^1];
            _closedTabs.RemoveAt(_closedTabs.Count - 1);
            if (!Directory.Exists(closed.Path)) continue;

            var group = Groups.Contains(closed.Group) ? closed.Group : _selectedGroup;
            if (group != _selectedGroup) SelectGroup(group);
            var tab = new FolderTabViewModel(closed.Path);
            group.Tabs.Insert(Math.Min(closed.Index, group.Tabs.Count), tab);
            Select(tab);
            return;
        }
    }

    /// <summary>タブを複製して、その右隣に開く(ペインタブはペインの構成ごと)。</summary>
    public void DuplicateTab(TabBase tab)
    {
        var group = Groups.FirstOrDefault(g => g.Tabs.Contains(tab));
        if (group == null) return;
        if (group != _selectedGroup) SelectGroup(group);

        if (tab is FolderTabViewModel folder)
        {
            var copy = new FolderTabViewModel(folder.Path);
            group.Tabs.Insert(group.Tabs.IndexOf(tab) + 1, copy);
            Select(copy);
        }
        else if (tab is PaneTabViewModel pane)
        {
            Select(tab); // 複製は、選択中のタブの右隣に作られる
            CreatePaneTab(pane.Panes.Select(p => p.Path).ToList());
        }
    }

    [RelayCommand]
    private void CloseTab(TabBase? tab)
    {
        tab ??= _selectedTab;
        if (tab == null) return;
        var group = Groups.FirstOrDefault(g => g.Tabs.Contains(tab));
        if (group == null || Groups.Sum(g => g.Tabs.Count) <= 1) return; // 最後の1枚は閉じない

        int index = group.Tabs.IndexOf(tab);
        if (tab is FolderTabViewModel closedFolder)
        {
            _closedTabs.Add(new ClosedTab(closedFolder.Path, group, index));
            if (_closedTabs.Count > ClosedTabLimit) _closedTabs.RemoveAt(0);
        }
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

    /// <summary>同じグループ内でタブの並び順を変える。tab を target の位置へ移す(選択は変えない)。</summary>
    public void ReorderTab(TabBase tab, TabBase target)
    {
        if (ReferenceEquals(tab, target)) return;
        var group = Groups.FirstOrDefault(g => g.Tabs.Contains(tab) && g.Tabs.Contains(target));
        if (group == null) return;
        group.Tabs.Move(group.Tabs.IndexOf(tab), group.Tabs.IndexOf(target));
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
        if (FavoriteTree.Folders(Favorites).Any(f => string.Equals(f.Path, p, StringComparison.OrdinalIgnoreCase))) return;
        string leaf = Path.GetFileName(p.TrimEnd('\\', '/'));
        Favorites.Add(new Favorite(string.IsNullOrEmpty(leaf) ? p : leaf, p));
    }

    [RelayCommand]
    private void AddCurrentToFavorites()
    {
        if (CurrentFolder != null) AddFavorite(CurrentFolder.Path);
    }

    public void RemoveFavorite(Favorite fav) => FavoriteTree.Remove(Favorites, fav);

    /// <summary>整理ウィンドウで整理したお気に入りに、まるごと置き換える。</summary>
    public void ReplaceFavorites(IEnumerable<Favorite> items)
    {
        var list = items.ToList();
        Favorites.Clear();
        foreach (var f in list) Favorites.Add(f);
    }

    private static Favorite? FavoriteFromState(FavoriteState s)
    {
        switch (s.Kind)
        {
            case FavoriteKind.Separator:
                return Favorite.NewSeparator();
            case FavoriteKind.Group:
            {
                var group = Favorite.NewGroup(string.IsNullOrWhiteSpace(s.Name) ? "グループ" : s.Name);
                foreach (var child in s.Children)
                {
                    var c = FavoriteFromState(child);
                    if (c != null && !c.IsGroup) group.Children.Add(c); // グループは入れ子にしない
                }
                return group;
            }
            default:
                return string.IsNullOrWhiteSpace(s.Path) ? null : new Favorite(string.IsNullOrWhiteSpace(s.Name) ? s.Path : s.Name, s.Path);
        }
    }

    private static FavoriteState FavoriteToState(Favorite f)
    {
        var s = new FavoriteState { Kind = f.Kind, Name = f.Name, Path = f.Path };
        foreach (var child in f.Children) s.Children.Add(FavoriteToState(child));
        return s;
    }

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
            SortColumn = SortSettings.Instance.Column,
            SortDescending = SortSettings.Instance.Descending,
            FoldersFirst = SortSettings.Instance.FoldersFirst,
            ViewMode = SortSettings.Instance.DefaultViewMode,
            AutoFolderSize = AutoFolderSize,
            ShowPreview = ShowPreview,
            SelectedGroupIndex = Groups.IndexOf(_selectedGroup),
        };
        foreach (var f in Favorites) state.Favorites.Add(FavoriteToState(f));
        foreach (var g in Groups)
        {
            var selected = ReferenceEquals(g, _selectedGroup) ? _selectedTab : g.LastSelected;
            var folderTabs = new List<string>();
            var tabSorts = new List<TabSortState>();
            int selectedIndex = 0;
            foreach (var t in g.Tabs)
            {
                if (t is FolderTabViewModel f)
                {
                    folderTabs.Add(f.Path);
                    tabSorts.Add(new TabSortState { Column = f.SortColumn, Descending = f.SortDescending, ViewMode = f.ViewMode });
                }
                if (ReferenceEquals(t, selected)) selectedIndex = Math.Max(folderTabs.Count - 1, 0);
            }
            state.Groups.Add(new GroupState { Name = g.Name, IsDefault = g.IsDefault, TabPaths = folderTabs, TabSorts = tabSorts, SelectedIndex = selectedIndex });
        }
        return state;
    }
}
