using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using KT_Explorer.Models;
using KT_Explorer.Services;
using KT_Explorer.ViewModels;
using KT_Explorer.Views;

namespace KT_Explorer;

public partial class MainWindow : Window
{
    private const string TabDragFormat = "KT_Explorer.Tab";

    private readonly MainViewModel _vm;
    private readonly AppState _state;
    private VirtualDesktopService? _desktop;
    private ExplorerCaptureService? _capture;
    private TrayService? _tray;
    private bool _exiting;

    private Point _gestureStart;
    private bool _gestureTracking;
    private Point _dragStart;
    private TabBase? _dragTab;

    public MainWindow()
    {
        InitializeComponent();
        _state = StateService.Load();
        _vm = new MainViewModel(_state);
        DataContext = _vm;

        Width = _state.Width;
        Height = _state.Height;
        if (!double.IsNaN(_state.Left) && !double.IsNaN(_state.Top)
            && _state.Left < SystemParameters.VirtualScreenWidth && _state.Top < SystemParameters.VirtualScreenHeight)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = _state.Left;
            Top = _state.Top;
        }
        if (_state.Maximized) WindowState = WindowState.Maximized;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _desktop = new VirtualDesktopService(this); // 仮想デスクトップ切替に追従

        // 外部で開かれた Explorer を取り込む(設定でオン/オフ)
        _capture = new ExplorerCaptureService();
        _capture.FolderCaptured += OnFolderCaptured;
        _capture.Enabled = _vm.CaptureExplorer;
        _vm.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(MainViewModel.CaptureExplorer)) _capture.Enabled = _vm.CaptureExplorer;
        };

        // タスクトレイ常駐
        _tray = new TrayService(
            show: ShowFromTray,
            exit: ExitApp,
            getCapture: () => _vm.CaptureExplorer,
            setCapture: value => _vm.CaptureExplorer = value);

        // Windows のサインアウト/シャットダウン時は、トレイに隠さず終了させる
        Application.Current.SessionEnding += (_, _) => _exiting = true;
    }

    private void OnFolderCaptured(string path)
    {
        _vm.ImportFolder(path);
        ShowFromTray();
    }

    /// <summary>ウィンドウを(隠れていても)現在のデスクトップの前面に表示する。</summary>
    public void ShowFromTray()
    {
        Show();
        if (WindowState == WindowState.Minimized) WindowState = _state.Maximized ? WindowState.Maximized : WindowState.Normal;
        _desktop?.FollowNow(); // 別の仮想デスクトップに居たままなら、今のデスクトップへ持ってくる
        Activate();
        Topmost = true;   // 前面化を確実にする(すぐ戻す)
        Topmost = false;
    }

    /// <summary>トレイメニューの「終了」: 常駐せずに本当に終了する。</summary>
    private void ExitApp()
    {
        _exiting = true;
        Close();
    }

    private void SaveState()
    {
        var s = _vm.ToState();
        var b = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        s.Left = b.Left; s.Top = b.Top; s.Width = b.Width; s.Height = b.Height;
        s.Maximized = WindowState == WindowState.Maximized;
        StateService.Save(s);
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        SaveState();

        // 閉じるボタン: 終了せずにトレイへ隠す(設定でオフにすると従来どおり終了)
        if (!_exiting && _vm.CloseToTray)
        {
            e.Cancel = true;
            Hide();
            _tray?.ShowHintOnce();
            return;
        }

        _tray?.Dispose();
        _desktop?.Dispose();
        _capture?.Dispose();
        base.OnClosing(e);
    }

    // ───────── マウス / キー ─────────

    // マウスのサイドボタン: 戻る / 進む
    private void Window_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        // ツリーで名前を入力中に、入力欄の外をクリックしたら確定する。
        // 入力欄がキーボードフォーカスを得られていない(起動直後など)場合は、フォーカスを失ったイベントが
        // 起きないため、クリックそのものを検知して確定する。
        if (_editingTreeNode is { IsEditing: true } editing
            && !(e.OriginalSource is DependencyObject src && FindAncestor<TextBox>(src) is { } tb && ReferenceEquals(tb.DataContext, editing)))
            CommitTreeRename(editing);

        if (e.ChangedButton == MouseButton.XButton1) { _vm.BackCommand.Execute(null); e.Handled = true; }
        else if (e.ChangedButton == MouseButton.XButton2) { _vm.ForwardCommand.Execute(null); e.Handled = true; }
    }

    // Ctrl+Tab / Ctrl+Shift+Tab: タブ切替(WPF 標準のフォーカス移動より先に処理する)
    // Ctrl+F: 絞込欄へ / Ctrl+L: パス欄へ
    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
        if (e.Key == Key.Tab)
        {
            _vm.CycleTab((Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? -1 : 1);
            e.Handled = true;
        }
        else if (e.Key == Key.F) { FilterBox.Focus(); FilterBox.SelectAll(); e.Handled = true; }
        else if (e.Key == Key.L) { PathBox.Focus(); PathBox.SelectAll(); e.Handled = true; }
        else if (e.Key == Key.Z && (Keyboard.Modifiers & ModifierKeys.Shift) == 0 && !IsTextEditing()) { Undo(); e.Handled = true; }
        else if ((e.Key == Key.Y || (e.Key == Key.Z && (Keyboard.Modifiers & ModifierKeys.Shift) != 0)) && !IsTextEditing())
        {
            Redo();
            e.Handled = true;
        }
    }

    // ───────── 元に戻す / やり直し ─────────

    /// <summary>文字を入力中(パス欄・絞込欄・名前の編集など)は、文字の入力側の「元に戻す」に任せる。</summary>
    private static bool IsTextEditing() => Keyboard.FocusedElement is System.Windows.Controls.Primitives.TextBoxBase;

    private void Undo() => ShowUndoResult(_vm.Undo(WindowHandle), "元に戻す");

    private void Redo() => ShowUndoResult(_vm.Redo(WindowHandle), "やり直し");

    private void Undo_Click(object sender, RoutedEventArgs e) => Undo();

    private void Redo_Click(object sender, RoutedEventArgs e) => Redo();

    private void ShowUndoResult(string? error, string title)
    {
        if (error != null) MessageBox.Show(error, title, MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    // ───────── マウスジェスチャ(右ボタンドラッグ: 左=戻る / 右=進む / 上=一つ上) ─────────

    private void Window_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        _gestureTracking = _vm.EnableGestures;
        if (_gestureTracking) _gestureStart = e.GetPosition(this);
    }

    private void Window_PreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_gestureTracking) return;
        _gestureTracking = false;

        const double MinDistance = 40;       // これ未満の動きは通常の右クリック(メニュー表示)
        const double DirectionRatio = 1.5;   // 縦横の差がはっきりしないものはジェスチャにしない
        var d = e.GetPosition(this) - _gestureStart;
        double ax = Math.Abs(d.X), ay = Math.Abs(d.Y);
        if (ax < MinDistance && ay < MinDistance) return;

        if (ax > ay * DirectionRatio)
        {
            if (d.X < 0) _vm.BackCommand.Execute(null); else _vm.ForwardCommand.Execute(null);
        }
        else if (-d.Y > ax * DirectionRatio)
        {
            _vm.UpCommand.Execute(null);
        }
        else
        {
            return;
        }
        e.Handled = true; // コンテキストメニューを出さない
    }

    // ───────── 設定メニュー ─────────

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = (UIElement)sender, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };

        MenuItem Check(string header, string property, string tip)
        {
            var item = new MenuItem { Header = header, IsCheckable = true, ToolTip = tip, StaysOpenOnClick = true };
            item.SetBinding(MenuItem.IsCheckedProperty,
                new System.Windows.Data.Binding(property) { Source = _vm, Mode = System.Windows.Data.BindingMode.TwoWay });
            return item;
        }

        menu.Items.Add(Check("ツリーをタブに連動", nameof(MainViewModel.SyncTreeWithTab), "タブを切り替えるたびにツリーの位置も追従します"));
        menu.Items.Add(Check("マウスジェスチャ(右ドラッグ)", nameof(MainViewModel.EnableGestures), "左=戻る / 右=進む / 上=一つ上の階層へ"));
        menu.Items.Add(Check("開いた Explorer を取り込む", nameof(MainViewModel.CaptureExplorer),
            "デスクトップ等から開いた Explorer をデフォルトグループのタブへ取り込みます"));
        menu.Items.Add(Check("閉じるボタンでトレイに常駐", nameof(MainViewModel.CloseToTray),
            "オフにすると、閉じるボタンでアプリを終了します(トレイの「終了」でも終了できます)"));
        var foldersFirst = new MenuItem
        {
            Header = "フォルダーを先頭にまとめる", IsCheckable = true, StaysOpenOnClick = true,
            IsChecked = SortSettings.Instance.FoldersFirst,
            ToolTip = "どの列で並べ替えても、フォルダーを一覧の先頭に置きます",
        };
        foldersFirst.Click += (_, _) => SortSettings.Instance.SetFoldersFirst(foldersFirst.IsChecked);
        menu.Items.Add(foldersFirst);

        // Windows 起動時の自動起動(レジストリが正)。起動時はウィンドウを出さず、トレイだけに常駐して始まる
        var startup = new MenuItem
        {
            Header = "Windows 起動時に自動起動", IsCheckable = true, StaysOpenOnClick = true,
            IsChecked = StartupService.IsEnabled,
            ToolTip = "サインインしたときに、トレイに常駐した状態で起動します(ウィンドウは開きません)",
        };
        startup.Click += (_, _) =>
        {
            if (!StartupService.SetEnabled(startup.IsChecked))
            {
                startup.IsChecked = StartupService.IsEnabled;
                MessageBox.Show("自動起動の設定を変更できませんでした。", "KT_Explorer", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        };
        menu.Items.Add(startup);
        menu.IsOpen = true;
    }

    // ───────── 絞込 / 検索 ─────────

    private void FilterBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _vm.CurrentFolder != null) { _vm.CurrentFolder.FilterText = ""; e.Handled = true; }
    }

    private void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (_vm.CurrentFolder == null) return;
        if (e.Key == Key.Enter)
        {
            _ = _vm.CurrentFolder.SearchAsync();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            _vm.CurrentFolder.Reload(); // 検索を中断して通常の一覧へ戻る
            e.Handled = true;
        }
    }

    // ───────── お気に入り ─────────

    /// <summary>「★ お気に入り」ボタン: 追加・編集・削除と、お気に入りの一覧を1つのメニューにまとめる。</summary>
    private void Favorites_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = (UIElement)sender, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        var favorites = _vm.Favorites.ToList();

        var add = new MenuItem { Header = "お気に入りに追加(現在のフォルダ)", IsEnabled = _vm.CurrentFolder != null };
        add.Click += (_, _) => _vm.AddCurrentToFavoritesCommand.Execute(null);
        menu.Items.Add(add);

        var edit = new MenuItem { Header = "編集", IsEnabled = favorites.Count > 0 };
        var remove = new MenuItem { Header = "削除", IsEnabled = favorites.Count > 0 };
        foreach (var fav in favorites)
        {
            var f = fav;
            var editItem = new MenuItem { Header = FavoriteLabel(f), ToolTip = f.Path };
            editItem.Click += (_, _) => EditFavorite(f);
            edit.Items.Add(editItem);
            var removeItem = new MenuItem { Header = FavoriteLabel(f), ToolTip = f.Path };
            removeItem.Click += (_, _) => _vm.RemoveFavorite(f);
            remove.Items.Add(removeItem);
        }
        menu.Items.Add(edit);
        menu.Items.Add(remove);
        menu.Items.Add(new Separator());

        if (favorites.Count == 0)
        {
            menu.Items.Add(new MenuItem { Header = "(お気に入りはありません)", IsEnabled = false });
        }
        foreach (var fav in favorites)
        {
            var f = fav;
            var item = new MenuItem { Header = FavoriteLabel(f), ToolTip = f.Path };
            item.Click += (_, _) => OpenFavorite(f, inNewTab: false);

            // 項目を右クリックすると、その項目だけの操作が出る
            var context = new ContextMenu();
            var openNew = new MenuItem { Header = "新しいタブで開く" };
            openNew.Click += (_, _) => OpenFavorite(f, inNewTab: true);
            var editOne = new MenuItem { Header = "編集…" };
            editOne.Click += (_, _) => EditFavorite(f);
            var removeOne = new MenuItem { Header = "削除" };
            removeOne.Click += (_, _) => _vm.RemoveFavorite(f);
            context.Items.Add(openNew);
            context.Items.Add(editOne);
            context.Items.Add(removeOne);
            item.ContextMenu = context;
            menu.Items.Add(item);
        }
        menu.IsOpen = true;
    }

    /// <summary>メニューの表示名。名前に「_」があっても、アクセスキーとして消えないよう TextBlock で包む。</summary>
    private static TextBlock FavoriteLabel(Favorite fav) => new() { Text = fav.Name };

    private void OpenFavorite(Favorite fav, bool inNewTab)
    {
        if (!_vm.OpenFavorite(fav, inNewTab))
            MessageBox.Show($"フォルダが見つかりません。\n{fav.Path}", "KT_Explorer");
    }

    private void EditFavorite(Favorite fav)
    {
        var name = InputDialog.Ask(this, "お気に入りを編集", "表示名:", fav.Name);
        if (name == null) return;
        var path = InputDialog.Ask(this, "お気に入りを編集", "フォルダのパス:", fav.Path);
        if (path == null) return;

        if (!System.IO.Directory.Exists(path))
        {
            MessageBox.Show("フォルダが見つからないため、パスは変更しませんでした。", "KT_Explorer");
            fav.Name = name;
            return;
        }
        fav.Name = name;
        fav.Path = FolderTabViewModel.Normalize(path);
    }

    private void PathBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        if (!_vm.NavigateToText(PathBox.Text))
            MessageBox.Show("フォルダが見つかりません。", "KT_Explorer");
        e.Handled = true;
    }

    // ───────── タブバー ─────────

    // タブホイール: タブバー上のホイールでタブ切替(上=前 / 下=次)
    private void TabBar_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        _vm.CycleTab(e.Delta > 0 ? -1 : 1);
        e.Handled = true;
    }

    private void GroupBar_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        _vm.CycleGroup(e.Delta > 0 ? -1 : 1);
        e.Handled = true;
    }

    private void TabBar_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TabBar.SelectedItem != null) TabBar.ScrollIntoView(TabBar.SelectedItem);
    }

    private void TabBar_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        var tab = (e.OriginalSource as DependencyObject) is { } d ? FindAncestor<ListBoxItem>(d)?.DataContext as TabBase : null;

        // 中クリックで閉じる
        if (e.ChangedButton == MouseButton.Middle && tab != null)
        {
            _vm.CloseTabCommand.Execute(tab);
            e.Handled = true;
            return;
        }

        // 左ボタン: ドラッグ開始候補を記録(✕ボタン上は除く)
        if (e.ChangedButton == MouseButton.Left && tab != null && FindAncestor<Button>((DependencyObject)e.OriginalSource) == null)
        {
            _dragStart = e.GetPosition(null);
            _dragTab = tab;
        }
        else
        {
            _dragTab = null;
        }
    }

    // タブをグループバーへドラッグして、別グループへ移動
    private void TabBar_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragTab == null) return;
        var delta = e.GetPosition(null) - _dragStart;
        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance) return;

        var tab = _dragTab;
        _dragTab = null;
        DragDrop.DoDragDrop(TabBar, new DataObject(TabDragFormat, tab), DragDropEffects.Move);
    }

    private void Group_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(TabDragFormat) ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private void Group_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(TabDragFormat) is TabBase tab && ((FrameworkElement)sender).DataContext is TabGroupViewModel group)
            _vm.MoveTabToGroup(tab, group);
        e.Handled = true;
    }

    // タブの右クリックメニュー(移動先グループは開くたびに組み立てる)
    private void TabItem_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not TabBase tab) return;

        var menu = new ContextMenu();
        var move = new MenuItem { Header = "グループへ移動" };
        foreach (var g in _vm.Groups)
        {
            var target = g;
            var item = new MenuItem { Header = g.Name, IsEnabled = !g.Tabs.Contains(tab) };
            item.Click += (_, _) => _vm.MoveTabToGroup(tab, target);
            move.Items.Add(item);
        }
        menu.Items.Add(move);
        var duplicate = new MenuItem { Header = "タブを複製" };
        duplicate.Click += (_, _) => _vm.DuplicateTab(tab);
        menu.Items.Add(duplicate);
        menu.Items.Add(new Separator());
        var close = new MenuItem { Header = "タブを閉じる", InputGestureText = "Ctrl+W" };
        close.Click += (_, _) => _vm.CloseTabCommand.Execute(tab);
        menu.Items.Add(close);
        var reopen = new MenuItem { Header = "閉じたタブを開き直す", InputGestureText = "Ctrl+Shift+T", IsEnabled = _vm.CanReopenClosedTab };
        reopen.Click += (_, _) => _vm.ReopenClosedTabCommand.Execute(null);
        menu.Items.Add(reopen);
        // メニューは自分で開く。ContextMenu プロパティに設定して WPF に開かせると、WPF のメニュー表示処理は
        // このハンドラーより先に走るため、1回目は何も出ず、2回目には前回の古いメニューが出てしまう。
        e.Handled = true;
        menu.PlacementTarget = fe;
        menu.Placement = e.CursorLeft < 0
            ? System.Windows.Controls.Primitives.PlacementMode.Bottom
            : System.Windows.Controls.Primitives.PlacementMode.MousePoint;
        menu.IsOpen = true;
    }

    // ───────── ファイルのドロップ先(ツリー / タブ) ─────────

    private IntPtr WindowHandle => new System.Windows.Interop.WindowInteropHelper(this).Handle;

    private static string? TreeDropTarget(object sender) =>
        sender is FrameworkElement { DataContext: TreeNodeViewModel { IsPlaceholder: false } node } ? node.FullPath : null;

    private void TreeItem_DragOver(object sender, DragEventArgs e)
    {
        // 入れ子の TreeViewItem では内側から順に届くので、最初(最も内側)の1回だけ処理する
        if (e.Handled) return;
        DropHelper.HandleDragOver(e, TreeDropTarget(sender));
    }

    private void TreeItem_Drop(object sender, DragEventArgs e)
    {
        if (e.Handled) return;
        DropHelper.HandleDrop(e, TreeDropTarget(sender), WindowHandle);
    }

    /// <summary>タブへのドロップ先: 通常タブはそのフォルダ、ペインタブは操作中のペイン。</summary>
    private static string? TabDropTarget(object sender) => (sender as FrameworkElement)?.DataContext switch
    {
        FolderTabViewModel f => f.Path,
        PaneTabViewModel p => p.ActivePane?.Path,
        _ => null,
    };

    private void TabItem_DragOver(object sender, DragEventArgs e)
    {
        // タブ同士のドラッグは並べ替え
        if (e.Data.GetDataPresent(TabDragFormat))
        {
            e.Effects = DragDropEffects.Move;
            e.Handled = true;
            return;
        }
        DropHelper.HandleDragOver(e, TabDropTarget(sender));
    }

    private void TabItem_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(TabDragFormat) is TabBase dragged)
        {
            if (((FrameworkElement)sender).DataContext is TabBase target) _vm.ReorderTab(dragged, target);
            e.Handled = true;
            return;
        }
        DropHelper.HandleDrop(e, TabDropTarget(sender), WindowHandle);
    }

    // ───────── タブグループ ─────────

    private void AddGroup_Click(object sender, RoutedEventArgs e)
    {
        var name = InputDialog.Ask(this, "タブグループを追加", "グループ名:");
        if (name != null) _vm.AddGroup(name);
    }

    private void RenameGroup_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is not TabGroupViewModel group) return;
        var name = InputDialog.Ask(this, "グループ名を変更", "新しいグループ名:", group.Name);
        if (name != null) _vm.RenameGroup(group, name);
    }

    private void DeleteGroup_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is not TabGroupViewModel group) return;
        if (group.IsDefault)
        {
            MessageBox.Show("デフォルトグループは削除できません。", "KT_Explorer");
            return;
        }
        var r = MessageBox.Show($"グループ「{group.Name}」と、中の {group.Tabs.Count} 個のタブを閉じます。よろしいですか?",
            "KT_Explorer", MessageBoxButton.OKCancel, MessageBoxImage.Question);
        if (r == MessageBoxResult.OK) _vm.DeleteGroup(group);
    }

    // ───────── ペイン(指定モード) ─────────

    private void SpecifyPane_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = (UIElement)sender, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };

        var fromTab = new MenuItem { Header = "既存タブから選択" };
        var candidates = _vm.AllFolderTabs().ToList();
        foreach (var (group, tab) in candidates)
        {
            var path = tab.Path;
            var item = new MenuItem { Header = $"{group.Name} / {tab.Title}  —  {tab.Path}" };
            item.Click += (_, _) => _vm.AddPane(path);
            fromTab.Items.Add(item);
        }
        fromTab.IsEnabled = candidates.Count > 0;
        menu.Items.Add(fromTab);

        var browse = new MenuItem { Header = "フォルダを指定…" };
        browse.Click += (_, _) =>
        {
            var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "右ペインに表示するフォルダ" };
            if (dlg.ShowDialog(this) == true) _vm.AddPane(dlg.FolderName);
        };
        menu.Items.Add(browse);

        menu.IsOpen = true;
    }

    // ───────── ツリー ─────────

    // 右クリック: クリックしたノードを対象にする(選択移動によるナビゲートはさせない)
    private TreeNodeViewModel? _contextNode;

    private void FolderTree_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        _contextNode = (e.OriginalSource is DependencyObject d ? FindAncestor<TreeViewItem>(d)?.DataContext : null) as TreeNodeViewModel;
        if (_contextNode == null || _contextNode.IsPlaceholder) e.Handled = true; // メニューを出さない
    }


    // ───────── ツリーでの名前の変更・削除・新規作成 ─────────

    /// <summary>
    /// メニューの項目から始める操作は、メニューが閉じて、フォーカスが元の項目へ戻る処理が済んでから行う
    /// (先に入力欄へフォーカスを移しても、メニューが閉じるときに奪い返されてしまうため)。
    /// </summary>
    private void AfterMenuClosed(Action action) =>
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle, action);

    private TreeNodeViewModel? _editingTreeNode; // 名前を入力中のノード(入力欄の外のクリックで確定するため)

    /// <summary>ツリーの項目を、その場で名前を入力できる状態にする(Windows 標準のエクスプローラーと同じ)。</summary>
    private void TreeRename(TreeNodeViewModel node)
    {
        if (node.IsPlaceholder || node.IsEditing) return;
        _editingTreeNode = node;
        node.EditName = node.Name;
        node.IsEditing = true; // 入力欄が表示され、IsVisibleChanged でフォーカス・全選択される
    }

    // 新しく作ったノードは、最初から編集状態で作られるため、表示の変化ではなく読み込み完了でも開始する
    private void TreeEditBox_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox { IsVisible: true } box) FocusTreeEditBox(box, attempts: 40);
    }

    private void TreeEditBox_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is true && sender is TextBox box) FocusTreeEditBox(box, attempts: 40);
    }

    /// <summary>
    /// 入力欄へフォーカスを移して全選択する。表示した直後や、新しく作ったノードの入力欄は、レイアウトが済むまで
    /// フォーカスを受けられないことがあるため、移せるまで少しずつ間を置いて繰り返す。
    /// </summary>
    private void FocusTreeEditBox(TextBox box, int attempts)
    {
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle, () =>
        {
            if (!box.IsVisible || box.DataContext is not TreeNodeViewModel { IsEditing: true }) return;
            box.BringIntoView();
            box.Focus();
            Keyboard.Focus(box);
            if (box.IsKeyboardFocusWithin) { box.SelectAll(); return; }
            if (attempts <= 1) return;

            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            timer.Tick += (_, _) => { timer.Stop(); FocusTreeEditBox(box, attempts - 1); };
            timer.Start();
        });
    }

    private void TreeEditBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox { DataContext: TreeNodeViewModel node }) return;
        if (e.Key == Key.Enter)
        {
            CommitTreeRename(node);
            FolderTree.Focus();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            CancelTreeRename(node);
            FolderTree.Focus();
            e.Handled = true;
        }
    }

    private void TreeEditBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is TextBox { DataContext: TreeNodeViewModel { IsEditing: true } node }) CommitTreeRename(node);
    }

    // ファイル名に使えない文字は入力させない
    private void TreeEditBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (e.Text.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0) e.Handled = true;
    }

    /// <summary>Esc: 名前の変更を取り消す(新しく作ったフォルダは、既定の名前のまま残る)。</summary>
    private static void CancelTreeRename(TreeNodeViewModel node) => node.IsEditing = false;

    /// <summary>Enter / フォーカスを失った: 入力した名前に変更する。</summary>
    private void CommitTreeRename(TreeNodeViewModel node)
    {
        if (!node.IsEditing) return; // 二重に呼ばれても(Enter のあとのフォーカス喪失など)1回だけ処理する
        node.IsEditing = false;

        string newName = node.EditName.Trim();
        if (newName.Length == 0 || newName == node.Name) return;

        string path = node.FullPath.TrimEnd('\\');
        _vm.PauseWatching(path); // 開いているタブの監視を止める(監視中のフォルダは名前を変えられない)
        try
        {
            string newPath = FileOperationService.Rename(path, newName);
            _vm.OnFolderRenamed(path, newPath); // 開いているタブ・ペイン、お気に入り、ツリーを追従させる
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "名前の変更", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            _vm.ResumeAllWatching();
        }
    }

    /// <summary>ツリー上の F2: 選択中のフォルダの名前を、その場で変更する。</summary>
    private void FolderTree_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.F2 || e.OriginalSource is TextBox) return;
        if (FolderTree.SelectedItem is TreeNodeViewModel { IsPlaceholder: false } node && CanModifyFolder(node.FullPath))
        {
            TreeRename(node);
            e.Handled = true;
        }
    }

    /// <summary>ドライブのルートなど、親を持たないものは、名前の変更・削除の対象にしない。</summary>
    private static bool CanModifyFolder(string path) => System.IO.Directory.GetParent(path.TrimEnd('\\')) != null;

    private void TreeDelete(TreeNodeViewModel node)
    {
        string path = node.FullPath.TrimEnd('\\');
        _vm.PauseWatching(path); // 開いているタブの監視を止める(監視中のフォルダは削除できない)
        var result = FileOperationService.Delete(new List<string> { path }, toRecycleBin: true, WindowHandle);
        if (result == FileOperationService.Result.Failed)
            MessageBox.Show("削除できませんでした。", "KT_Explorer", MessageBoxButton.OK, MessageBoxImage.Warning);
        _vm.AfterFileChange(); // 無くなったフォルダを開いているタブ・ペインは、親フォルダへ移る
    }

    private void TreeNewFolder(TreeNodeViewModel node)
    {
        string created;
        try
        {
            created = FileOperationService.CreateFolder(node.FullPath, "新しいフォルダー");
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "新しいフォルダー", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        node.EnsureChildren();
        _vm.RefreshTree();
        node.IsExpanded = true;

        // 作ったフォルダの名前を、その場で入力する(Esc で、既定の名前のまま)
        var child = node.Children.FirstOrDefault(c => string.Equals(c.FullPath, created, StringComparison.Ordinal));
        if (child == null) return;
        TreeRename(child);
    }

    private ContextMenu? _treeMenu;

    /// <summary>ツリーの右クリックメニュー。一覧と同じ作り(独自メニュー + 「その他のオプション」)。Shift+右クリックは標準メニューを直接出す。</summary>
    private void FolderTree_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        // キーボード(メニューキー / Shift+F10)で開いたときは、マウスの右クリックを経ていないので、選択中のノードを対象にする
        var node = e.CursorLeft < 0 ? FolderTree.SelectedItem as TreeNodeViewModel : _contextNode;
        if (node == null || node.IsPlaceholder || !System.IO.Directory.Exists(node.FullPath)) { e.Handled = true; return; }
        string path = node.FullPath;

        if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0)
        {
            e.Handled = true;
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, () => ShowTreeShellMenu(node, extended: true));
            return;
        }

        var menu = new ContextMenu();
        MenuItem Add(string header, Action action, string? gesture = null, bool enabled = true, bool bold = false)
        {
            var item = new MenuItem { Header = header, IsEnabled = enabled, InputGestureText = gesture ?? "" };
            if (bold) item.FontWeight = FontWeights.Bold;
            item.Click += (_, _) => action();
            menu.Items.Add(item);
            return item;
        }

        Add("開く", () => _vm.NavigateToText(path), bold: true);
        Add("新しいタブで開く", () => _vm.OpenInNewTab(path));
        Add("お気に入りに追加", () => _vm.AddFavorite(path));
        menu.Items.Add(new Separator());
        Add("切り取り", () => ClipboardService.SetFiles(new[] { path }, cut: true));
        Add("コピー", () => ClipboardService.SetFiles(new[] { path }, cut: false));
        Add("貼り付け", () => FolderActions.PasteInto(path, WindowHandle), enabled: ClipboardService.HasFiles());
        menu.Items.Add(new Separator());
        var create = new MenuItem { Header = "新規作成" };
        var newFolder = new MenuItem { Header = "フォルダー" };
        newFolder.Click += (_, _) => AfterMenuClosed(() => TreeNewFolder(node));
        create.Items.Add(newFolder);
        menu.Items.Add(create);
        // ドライブのルートなど、親を持たないものは、名前の変更・削除の対象にしない
        bool canModify = CanModifyFolder(path);
        Add("名前の変更", () => AfterMenuClosed(() => TreeRename(node)), enabled: canModify);
        Add("削除", () => TreeDelete(node), enabled: canModify);
        menu.Items.Add(new Separator());
        Add("パスをコピー", () => FolderActions.CopyPathText(new[] { path }));
        Add("ここでターミナルを開く", () => FolderActions.OpenTerminal(path));
        Add("プロパティ", () => FolderActions.ShowProperties(path));
        menu.Items.Add(new Separator());
        Add("その他のオプション", () =>
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, () => ShowTreeShellMenu(node, extended: false)),
            "Shift+右クリック");

        // メニューは自分で開く(ContextMenu プロパティに設定すると、1回目に出ない・古いメニューが出る。一覧と同じ理由)
        e.Handled = true;
        if (_treeMenu != null) _treeMenu.IsOpen = false;
        menu.PlacementTarget = FolderTree;
        menu.Placement = e.CursorLeft < 0
            ? System.Windows.Controls.Primitives.PlacementMode.Center
            : System.Windows.Controls.Primitives.PlacementMode.MousePoint;
        _treeMenu = menu;
        menu.IsOpen = true;
    }

    private void ShowTreeShellMenu(TreeNodeViewModel node, bool extended)
    {
        string path = node.FullPath;
        bool canModify = System.IO.Directory.GetParent(path.TrimEnd('\\')) != null;
        try
        {
            ShellContextMenu.Show(WindowHandle, new[] { path }, null, extended, verb =>
            {
                // 標準の画面を前提とする動詞は、KT_Explorer 自身の処理に置き換える(開く=このタブで開く、名前の変更・削除=開いているタブへの追従つき)
                switch (verb.ToLowerInvariant())
                {
                    case "open":
                        _vm.NavigateToText(path);
                        return true;
                    case "rename" when canModify:
                        TreeRename(node);
                        return true;
                    case "delete" when canModify:
                        TreeDelete(node);
                        return true;
                    default:
                        return false;
                }
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Windows 標準のメニューを表示できませんでした。\n{ex.Message}", "KT_Explorer",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static T? FindAncestor<T>(DependencyObject? d) where T : DependencyObject
    {
        while (d != null)
        {
            if (d is T t) return t;
            d = VisualTreeHelper.GetParent(d);
        }
        return null;
    }
}
