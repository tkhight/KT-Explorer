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
    private const string GroupDragFormat = "KT_Explorer.TabGroup";
    private TabGroupViewModel? _dragGroup;
    private Point _groupDragStart;

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
        Deactivated += (_, _) => EndGestureTracking(); // 右ボタンを押したまま別のウィンドウへ移ったときの保険
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

        _previewWidth = _state.PreviewWidth > 0 ? _state.PreviewWidth : 320;
        ApplyPreviewColumns();
        _vm.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(MainViewModel.ShowPreview)) ApplyPreviewColumns();
            else if (args.PropertyName == nameof(MainViewModel.MultiRowTabs)) ApplyTabLayout();
        };
        ApplyTabLayout();

        // ツリーは、見えているノードだけを作る(仮想化)ので、選択したノードが見える位置へ、スクロールする
        _vm.TreeNodeSynced += node => Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () => ScrollTreeTo(node));
        Loaded += (_, _) =>
        {
            if (_vm.LastSyncedTreeNode is { } n) Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () => ScrollTreeTo(n));
        };
    }

    // ───────── タブバーの並べ方(1 段 / 複数段) ─────────

    /// <summary>
    /// フォルダタブの並べ方を切り替える。1 段: 横に並べ、あふれた分は横にスクロール。
    /// 複数段: 幅に収まらない分を、次の段へ折り返す(スクロールせず、すべてのタブが見える)。
    /// </summary>
    private void ApplyTabLayout()
    {
        bool multi = _vm.MultiRowTabs;
        var panel = new FrameworkElementFactory(multi ? typeof(WrapPanel) : typeof(StackPanel));
        if (!multi) panel.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
        TabBar.ItemsPanel = new ItemsPanelTemplate(panel);
        ScrollViewer.SetHorizontalScrollBarVisibility(TabBar, multi ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto);
        if (TabBar.SelectedItem != null) Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () => TabBar.ScrollIntoView(TabBar.SelectedItem));
    }

    // ───────── ツリーのスクロール(仮想化) ─────────

    private static readonly System.Reflection.PropertyInfo? ItemsHostProperty =
        typeof(ItemsControl).GetProperty("ItemsHost", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

    /// <summary>
    /// ツリーを、target のノードが見える位置へスクロールする。仮想化しているため、見えていないノードは、画面の要素が作られていない。
    /// ルートから target まで、親を順にたどり、各階層で、そのノードの位置を表示させて(要素を作らせて)から、次の階層へ進む。
    /// </summary>
    private void ScrollTreeTo(TreeNodeViewModel target)
    {
        var chain = new List<TreeNodeViewModel>();
        for (var n = target; n != null; n = n.Parent) chain.Insert(0, n);

        ItemsControl parent = FolderTree;
        foreach (var node in chain)
        {
            var container = parent.ItemContainerGenerator.ContainerFromItem(node) as TreeViewItem;
            if (container == null)
            {
                parent.UpdateLayout();
                int index = parent.Items.IndexOf(node);
                if (index < 0) return;
                (ItemsHostProperty?.GetValue(parent) as VirtualizingPanel)?.BringIndexIntoViewPublic(index);
                parent.UpdateLayout();
                container = parent.ItemContainerGenerator.ContainerFromItem(node) as TreeViewItem;
            }
            if (container == null) return;
            if (ReferenceEquals(node, target)) { container.BringIntoView(); return; }
            if (!container.IsExpanded) container.IsExpanded = true;
            container.ApplyTemplate();
            parent = container;
        }
    }

    // ───────── メニューバー: 編集 ─────────

    /// <summary>
    /// 編集メニューの対象の入力欄。パス欄・絞込欄・検索欄・名前の入力中などの、文字の入力欄にフォーカスがあるときは、その入力欄(文字の操作)。
    /// なければ null(ファイルの操作)。メニューを開いている間も、論理的なフォーカスは、直前の場所に残る。
    /// </summary>
    private TextBox? ActiveTextBox() => FocusManager.GetFocusedElement(this) as TextBox ?? Keyboard.FocusedElement as TextBox;

    /// <summary>編集メニューを開くとき、いまできる項目だけを有効にする。</summary>
    private void EditMenu_SubmenuOpened(object sender, RoutedEventArgs e)
    {
        if (!IsOwnOpen(sender, e)) return;
        var tb = ActiveTextBox();
        var tab = Target;
        bool folder = tab != null && System.IO.Directory.Exists(tab.Path);
        bool hasSelection = tab != null && tab.Selection.Count > 0;

        if (tb != null)
        {
            MenuUndo.IsEnabled = tb.CanUndo;
            MenuRedo.IsEnabled = tb.CanRedo;
            MenuCut.IsEnabled = tb.SelectionLength > 0 && !tb.IsReadOnly;
            MenuCopy.IsEnabled = tb.SelectionLength > 0;
            MenuPaste.IsEnabled = !tb.IsReadOnly && Clipboard.ContainsText();
            MenuDelete.IsEnabled = !tb.IsReadOnly && (tb.SelectionLength > 0 || tb.CaretIndex < tb.Text.Length);
            MenuSelectAll.IsEnabled = tb.Text.Length > 0;
            MenuUndo.ToolTip = MenuRedo.ToolTip = null;
        }
        else
        {
            MenuUndo.IsEnabled = _vm.CanUndo;
            MenuRedo.IsEnabled = _vm.CanRedo;
            MenuUndo.ToolTip = _vm.UndoToolTip;
            MenuRedo.ToolTip = _vm.RedoToolTip;
            MenuCut.IsEnabled = hasSelection;
            MenuCopy.IsEnabled = hasSelection;
            MenuPaste.IsEnabled = folder && !tab!.IsShowingSearch && ClipboardService.GetFiles() != null;
            MenuDelete.IsEnabled = hasSelection;
            MenuSelectAll.IsEnabled = folder;
        }
    }

    private void MenuUndo_Click(object sender, RoutedEventArgs e) { if (ActiveTextBox() is { } tb) tb.Undo(); else Undo(); }

    private void MenuRedo_Click(object sender, RoutedEventArgs e) { if (ActiveTextBox() is { } tb) tb.Redo(); else Redo(); }

    private void MenuCut_Click(object sender, RoutedEventArgs e) { if (ActiveTextBox() is { } tb) tb.Cut(); else Target?.RequestEdit(EditCommand.Cut); }

    private void MenuCopy_Click(object sender, RoutedEventArgs e) { if (ActiveTextBox() is { } tb) tb.Copy(); else Target?.RequestEdit(EditCommand.Copy); }

    private void MenuPaste_Click(object sender, RoutedEventArgs e) { if (ActiveTextBox() is { } tb) tb.Paste(); else Target?.RequestEdit(EditCommand.Paste); }

    private void MenuDelete_Click(object sender, RoutedEventArgs e)
    {
        if (ActiveTextBox() is { } tb) System.Windows.Documents.EditingCommands.Delete.Execute(null, tb);
        else Target?.RequestEdit(EditCommand.Delete);
    }

    private void MenuSelectAll_Click(object sender, RoutedEventArgs e) { if (ActiveTextBox() is { } tb) tb.SelectAll(); else Target?.RequestEdit(EditCommand.SelectAll); }

    // ───────── メニューバー: ファイル ─────────

    /// <summary>操作の対象にするフォルダ(操作中のタブ・ペインのフォルダ)。</summary>
    private FolderTabViewModel? Target => _vm.CurrentFolder;

    /// <summary>ファイルメニューを開くとき、いまできない項目を無効にする。</summary>
    private void FileMenu_SubmenuOpened(object sender, RoutedEventArgs e)
    {
        bool hasFolder = Target != null && System.IO.Directory.Exists(Target.Path);
        MenuOpenTerminal.IsEnabled = hasFolder;
        MenuNew.IsEnabled = hasFolder && !Target!.IsShowingSearch; // 検索結果の上には作れない(作る場所が決まらない)
        MenuProperties.IsEnabled = hasFolder;
    }

    private void MenuOpenTerminal_Click(object sender, RoutedEventArgs e)
    {
        // 選択がフォルダ 1 つならその中で、そうでなければ、表示中のフォルダで開く
        if (Target != null) FolderActions.OpenTerminal(TerminalFolder(Target));
    }

    /// <summary>ターミナルを開くフォルダ: 選択が 1 つのフォルダならそのフォルダ、それ以外は表示中のフォルダ。</summary>
    internal static string TerminalFolder(FolderTabViewModel tab)
    {
        if (tab.Selection.Count == 1 && tab.Selection[0].IsDirectory) return tab.Selection[0].FullPath;
        return tab.Path;
    }

    private void MenuNewFolder_Click(object sender, RoutedEventArgs e) => Target?.RequestNewItem(folder: true);

    private void MenuNewTextFile_Click(object sender, RoutedEventArgs e) => Target?.RequestNewItem(folder: false);

    private void MenuProperties_Click(object sender, RoutedEventArgs e)
    {
        if (Target == null) return;
        // 選択が 1 つならその項目の、それ以外は表示中のフォルダのプロパティ
        FolderActions.ShowProperties(Target.Selection.Count == 1 ? Target.Selection[0].FullPath : Target.Path);
    }

    private void MenuExit_Click(object sender, RoutedEventArgs e) => ExitApp();

    /// <summary>
    /// WinMerge で比較する対象。選択が 2 つなら、その 2 つ。選択が 2 つでなく、ペインが 2 つ以上あれば、操作中のペインと、その右隣(右端なら左端)のペインのフォルダ。
    /// それ以外は、何も渡さず WinMerge だけを起動する。
    /// </summary>
    private List<string> WinMergeTargets()
    {
        var tab = Target;
        if (tab == null) return new List<string>();
        if (tab.Selection.Count == 2) return tab.Selection.Select(s => s.FullPath).ToList();
        if (tab.Selection.Count <= 1 && _vm.SelectedTab is PaneTabViewModel pane && pane.Panes.Count >= 2 && pane.ActivePane != null)
        {
            int i = pane.Panes.IndexOf(pane.ActivePane);
            var other = pane.Panes[(i + 1) % pane.Panes.Count];
            return new List<string> { pane.ActivePane.Path, other.Path };
        }
        return new List<string>();
    }

    private void MenuWinMerge_Click(object sender, RoutedEventArgs e)
    {
        var targets = WinMergeTargets();
        var args = WinMergeService.BuildArguments(targets);
        if (args == null)
        {
            MessageBox.Show("ファイルとフォルダーは、いっしょに比較できません。同じ種類の 2 つを選んでください。", "KT_Explorer", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (!WinMergeService.Launch(args, out var error))
            MessageBox.Show(error, "KT_Explorer", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    // ───────── 履歴(最近開いたフォルダ・よく開くフォルダ) ─────────

    /// <summary>メニューを開くとき、入れ子のメニューから伝わってきた通知は無視する(自分自身が開かれたときだけ、中身を作り直す)。</summary>
    private static bool IsOwnOpen(object sender, RoutedEventArgs e) => ReferenceEquals(e.OriginalSource, sender);

    /// <summary>「履歴」メニュー: 最近開いたフォルダと、よく開くフォルダ。クリックで開き、Ctrl+クリックで新しいタブで開く。</summary>
    private void HistoryMenu_SubmenuOpened(object sender, RoutedEventArgs e)
    {
        if (!IsOwnOpen(sender, e)) return;
        var menu = (MenuItem)sender;
        menu.Items.Clear();

        // 存在するフォルダだけを出す(消えた・つながらない場所は、出さない)
        static IEnumerable<FolderVisit> Existing(IEnumerable<FolderVisit> visits, int count) =>
            visits.Where(v => System.IO.Directory.Exists(v.Path)).Take(count);

        void AddSection(string title, IEnumerable<FolderVisit> items, bool showCount)
        {
            var list = items.ToList();
            if (list.Count == 0) return;
            if (menu.Items.Count > 0) menu.Items.Add(new Separator());
            menu.Items.Add(new MenuItem { Header = title, IsEnabled = false, FontWeight = FontWeights.SemiBold });
            foreach (var visit in list)
            {
                var v = visit;
                string leaf = System.IO.Path.GetFileName(v.Path.TrimEnd('\\'));
                string name = string.IsNullOrEmpty(leaf) ? v.Path : leaf;
                var item = new MenuItem
                {
                    Header = new TextBlock { Text = showCount ? $"{name}    {v.Path}  ({v.Count} 回)" : $"{name}    {v.Path}" },
                    ToolTip = v.Path,
                };
                item.Click += (_, _) =>
                {
                    if ((Keyboard.Modifiers & ModifierKeys.Control) != 0) _vm.OpenInNewTab(v.Path);
                    else if (!_vm.NavigateToText(v.Path)) MessageBox.Show($"フォルダが見つかりません。\n{v.Path}", "KT_Explorer");
                };
                menu.Items.Add(item);
            }
        }

        AddSection("最近開いたフォルダ", Existing(FolderHistory.Recent(60), 15), showCount: false);
        AddSection("よく開くフォルダ", Existing(FolderHistory.Frequent(40), 10), showCount: true);
        if (menu.Items.Count == 0) menu.Items.Add(new MenuItem { Header = "(履歴はまだありません)", IsEnabled = false });
        else
        {
            menu.Items.Add(new Separator());
            var clear = new MenuItem { Header = "履歴を消去" };
            clear.Click += (_, _) => FolderHistory.Clear();
            menu.Items.Add(clear);
        }
    }

    // ───────── 表示の切り替え・並べ替え ─────────

    /// <summary>「表示」メニュー: 一覧の表示(詳細・アイコン・一覧)と、並べ替え(アイコン・一覧には列見出しが無いため、ここからも選べる)。</summary>
    private void ViewMenu_SubmenuOpened(object sender, RoutedEventArgs e)
    {
        if (!IsOwnOpen(sender, e)) return;
        var menu = (MenuItem)sender;
        menu.Items.Clear();
        var tab = _vm.CurrentFolder;
        if (tab == null) { menu.Items.Add(new MenuItem { Header = "(フォルダを開いていません)", IsEnabled = false }); AddViewSettings(menu.Items); return; }

        // 表示の種類。上から: 大アイコン / 中アイコン / 小アイコン / 一覧 / 詳細(アイコンの大きさは、全タブ共通の設定)
        void Add(string header, string gesture, bool isChecked, Action apply)
        {
            var item = new MenuItem { Header = header, InputGestureText = gesture, IsChecked = isChecked };
            item.Click += (_, _) => apply();
            menu.Items.Add(item);
        }
        void Icons(string header, IconSize size)
        {
            // Ctrl+Shift+2 は、いまの大きさのアイコン表示
            string gesture = ViewSettings.IconSize == size ? "Ctrl+Shift+2" : "";
            Add(header, gesture, tab.ViewMode == ViewMode.Icons && ViewSettings.IconSize == size, () =>
            {
                ViewSettings.IconSize = size;
                tab.SetViewMode(ViewMode.Icons);
            });
        }
        Icons("大アイコン", IconSize.Large);
        Icons("中アイコン", IconSize.Medium);
        Icons("小アイコン", IconSize.Small);
        Add("一覧", "Ctrl+Shift+3", tab.ViewMode == ViewMode.List, () => tab.SetViewMode(ViewMode.List));
        Add("詳細", "Ctrl+Shift+1", tab.ViewMode == ViewMode.Details, () => tab.SetViewMode(ViewMode.Details));
        menu.Items.Add(new Separator());

        var sort = new MenuItem { Header = "並べ替え" };
        foreach (var (column, label) in new[] { (SortColumn.Name, "名前"), (SortColumn.Modified, "更新日時"), (SortColumn.Type, "種類"), (SortColumn.Size, "サイズ") })
        {
            var c = column;
            var item = new MenuItem { Header = label, IsChecked = tab.SortColumn == c };
            item.Click += (_, _) => tab.ChooseSort(c, tab.SortDescending);
            sort.Items.Add(item);
        }
        sort.Items.Add(new Separator());
        var asc = new MenuItem { Header = "昇順", IsChecked = !tab.SortDescending };
        asc.Click += (_, _) => tab.ChooseSort(tab.SortColumn, false);
        var desc = new MenuItem { Header = "降順", IsChecked = tab.SortDescending };
        desc.Click += (_, _) => tab.ChooseSort(tab.SortColumn, true);
        sort.Items.Add(asc);
        sort.Items.Add(desc);
        menu.Items.Add(sort);
        AddViewSettings(menu.Items);
    }

    // ───────── プレビューウィンドウ ─────────

    private double _previewWidth = 320;

    /// <summary>プレビューの表示・非表示に合わせて、列の幅と部品の表示を切り替える(幅は、閉じても覚えておく)。</summary>
    private void ApplyPreviewColumns()
    {
        if (_vm.ShowPreview)
        {
            PreviewSplitterColumn.Width = new GridLength(5);
            PreviewColumn.Width = new GridLength(Math.Max(_previewWidth, 160));
            PreviewColumn.MinWidth = 160;
            PreviewSplitter.Visibility = Visibility.Visible;
            PreviewPanel.Visibility = Visibility.Visible;
        }
        else
        {
            if (PreviewColumn.ActualWidth > 0) _previewWidth = PreviewColumn.ActualWidth;
            PreviewColumn.MinWidth = 0;
            PreviewSplitterColumn.Width = new GridLength(0);
            PreviewColumn.Width = new GridLength(0);
            PreviewSplitter.Visibility = Visibility.Collapsed;
            PreviewPanel.Visibility = Visibility.Collapsed;
        }
    }

    private void PreviewSplitter_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
    {
        if (PreviewColumn.ActualWidth > 0) _previewWidth = PreviewColumn.ActualWidth;
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
        s.PreviewWidth = _vm.ShowPreview && PreviewColumn.ActualWidth > 0 ? PreviewColumn.ActualWidth : _previewWidth;
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

        if (e.ChangedButton == MouseButton.Left)
        {
            // ロッカージェスチャ: 右ボタンを押したまま左クリック = いまのタブを閉じる(最後の1枚は閉じない)。
            // 右ボタンがまだ押されているか(離しを見逃していないか)を確かめ、そうでなければ追跡をやめる(保険)
            if (_wheelGesture.Rocker(e.RightButton == MouseButtonState.Pressed))
            {
                _vm.CloseTabCommand.Execute(null);
                e.Handled = true; // 左クリック本来の動き(選択など)はさせない
                return;
            }
            EndGestureTracking();
        }
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
        else if ((e.Key == Key.C || e.Key == Key.X || e.Key == Key.V) && (Keyboard.Modifiers & (ModifierKeys.Shift | ModifierKeys.Alt)) == 0
                 && !IsTextEditing() && !IsFocusInTree() && _vm.CurrentFolder is { } active)
        {
            // コピー・切り取り・貼り付けは、キーボードフォーカスのある一覧ではなく、「操作中のタブ・ペイン」へ送る。
            // タブバーやペインの枠(ヘッダー)をクリックしても、キーボードフォーカスは前の一覧に残るため、
            // 一覧の KeyDown に任せると、前のタブ・ペインに貼り付けてしまう。
            active.RequestEdit(e.Key == Key.C ? EditCommand.Copy : e.Key == Key.X ? EditCommand.Cut : EditCommand.Paste);
            e.Handled = true;
        }
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

    /// <summary>キーボードフォーカスが、ツリーの中にあるか(ツリーでの操作は、一覧のコピー・貼り付けとは別)。</summary>
    private static bool IsFocusInTree() => Keyboard.FocusedElement is DependencyObject d && FindAncestor<TreeView>(d) != null;

    private void Undo() => ShowUndoResult(_vm.Undo(WindowHandle), "元に戻す");

    private void Redo() => ShowUndoResult(_vm.Redo(WindowHandle), "やり直し");

    private void Undo_Click(object sender, RoutedEventArgs e) => Undo();

    private void Redo_Click(object sender, RoutedEventArgs e) => Redo();

    private void ShowUndoResult(string? error, string title)
    {
        if (error != null) MessageBox.Show(error, title, MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    // ───────── マウスジェスチャ(右ボタンドラッグ: 左=戻る / 右=進む / 上=一つ上) ─────────

    private readonly WheelGestureTracker _wheelGesture = new();   // 右ボタン+ホイール(上=左のタブ / 下=右のタブ。Ctrl つきは、前・次のタブグループ)

    /// <summary>右ボタンを押している途中で、ウィンドウが非アクティブになった・左ボタンが押された: 右ボタンを離したのを見逃した可能性があるので、ジェスチャを終える。</summary>
    private void EndGestureTracking() { _gestureTracking = false; _wheelGesture.Cancel(); }

    private void Window_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        _gestureTracking = _vm.EnableGestures;
        _wheelGesture.RightDown(_vm.EnableGestures);
        if (_gestureTracking) _gestureStart = e.GetPosition(this);
    }

    /// <summary>右ボタンを押したままホイールを回すと、左のタブ(上へ回す)・右のタブ(下へ回す)へ切り替える(タブバーのホイールと同じ動き)。</summary>
    private void Window_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        switch (_wheelGesture.Wheel(e.Delta, ctrl: (Keyboard.Modifiers & ModifierKeys.Control) != 0))
        {
            case WheelAction.PreviousTab: _vm.CycleTab(-1); break;
            case WheelAction.NextTab: _vm.CycleTab(1); break;
            case WheelAction.PreviousGroup: _vm.CycleGroup(-1); break;
            case WheelAction.NextGroup: _vm.CycleGroup(1); break;
            default: return;
        }
        e.Handled = true; // ホイールの本来の動き(スクロール・タブ切り替え)はさせない
    }

    private void Window_PreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        bool wheelUsed = _wheelGesture.RightUp();
        if (!_gestureTracking) return;
        _gestureTracking = false;
        if (wheelUsed) { e.Handled = true; return; } // ホイールで操作したときは、右クリックのメニューを出さない

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

    // ───────── 設定の項目(「表示」「ツール」メニューに振り分け) ─────────

    /// <summary>設定(オン/オフ)の項目。MainViewModel のプロパティと、双方向に連動する。</summary>
    private MenuItem SettingCheck(string header, string property, string tip)
    {
        var item = new MenuItem { Header = header, IsCheckable = true, ToolTip = tip, StaysOpenOnClick = true };
        item.SetBinding(MenuItem.IsCheckedProperty,
            new System.Windows.Data.Binding(property) { Source = _vm, Mode = System.Windows.Data.BindingMode.TwoWay });
        return item;
    }

    /// <summary>「表示」メニューの設定: ツリーをタブに連動 / プレビューを表示 / フォルダーを先頭にまとめる / タブを複数段で表示 / オーバーレイアイコンを表示。</summary>
    private void AddViewSettings(ItemCollection items)
    {
        items.Add(new Separator());
        items.Add(SettingCheck("ツリーをタブに連動", nameof(MainViewModel.SyncTreeWithTab), "タブを切り替えるたびにツリーの位置も追従します"));
        items.Add(SettingCheck("プレビューを表示", nameof(MainViewModel.ShowPreview),
            "一覧で選択したファイルの内容(画像・テキスト・情報)を、右側に表示します (Alt+P)"));
        var foldersFirst = new MenuItem
        {
            Header = "フォルダーを先頭にまとめる", IsCheckable = true, StaysOpenOnClick = true,
            IsChecked = SortSettings.Instance.FoldersFirst,
            ToolTip = "どの列で並べ替えても、フォルダーを一覧の先頭に置きます",
        };
        foldersFirst.Click += (_, _) => SortSettings.Instance.SetFoldersFirst(foldersFirst.IsChecked);
        items.Add(foldersFirst);
        items.Add(SettingCheck("タブを複数段で表示", nameof(MainViewModel.MultiRowTabs),
            "フォルダタブが幅に収まらないとき、横にスクロールさせず、次の段へ折り返して、すべてのタブを見えるようにします"));
        items.Add(SettingCheck("オーバーレイアイコンを表示", nameof(MainViewModel.ShowOverlayIcons),
            "アイコンに、OneDrive の同期状態・SVN の状態・ショートカットの矢印などを重ねます(エクスプローラーと同じ)。重いときは、オフにしてください"));
    }

    /// <summary>「ツール」メニュー: マウスジェスチャ / 開いた Explorer を取り込む / 閉じるボタンでトレイに常駐 / フォルダーのサイズを自動計算 / Windows 起動時に自動起動。</summary>
    private void ToolsMenu_SubmenuOpened(object sender, RoutedEventArgs e)
    {
        if (!IsOwnOpen(sender, e)) return;
        var menu = (MenuItem)sender;
        menu.Items.Clear();

        menu.Items.Add(SettingCheck("マウスジェスチャ(右ドラッグ)", nameof(MainViewModel.EnableGestures), "左=戻る / 右=進む / 上=一つ上の階層へ / 右ボタン+ホイール: 上=左のタブ・下=右のタブ / Ctrl も押すと、前・次のタブグループ / 右ボタンを押したまま左クリック: いまのタブを閉じる"));
        menu.Items.Add(SettingCheck("開いた Explorer を取り込む", nameof(MainViewModel.CaptureExplorer),
            "デスクトップ等から開いた Explorer をデフォルトグループのタブへ取り込みます"));
        menu.Items.Add(SettingCheck("閉じるボタンでトレイに常駐", nameof(MainViewModel.CloseToTray),
            "オフにすると、閉じるボタンでアプリを終了します(トレイの「終了」でも終了できます)"));
        menu.Items.Add(SettingCheck("フォルダーのサイズを自動計算", nameof(MainViewModel.AutoFolderSize),
            "一覧を開くと、フォルダーの合計サイズをバックグラウンドで計算して「サイズ」列に表示します(フォルダーが多い場所や、ネットワークドライブでは重くなります)。右クリックの「フォルダーのサイズを計算」でも、必要なときだけ計算できます"));

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

    /// <summary>「お気に入り」メニュー: 追加・整理と、お気に入りの一覧(グループはサブメニュー、区切り線つき)を1つのメニューにまとめる。</summary>
    private void FavoritesMenu_SubmenuOpened(object sender, RoutedEventArgs e)
    {
        if (!IsOwnOpen(sender, e)) return;
        var menu = (MenuItem)sender;
        menu.Items.Clear();

        var add = new MenuItem { Header = "お気に入りに追加(現在のフォルダ)", IsEnabled = _vm.CurrentFolder != null };
        add.Click += (_, _) => _vm.AddCurrentToFavoritesCommand.Execute(null);
        menu.Items.Add(add);

        var organize = new MenuItem { Header = "お気に入りを整理…(並べ替え・グループ・区切り線・編集・削除)" };
        organize.Click += (_, _) => Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, ManageFavorites);
        menu.Items.Add(organize);
        menu.Items.Add(new Separator());

        if (_vm.Favorites.Count == 0)
            menu.Items.Add(new MenuItem { Header = "(お気に入りはありません)", IsEnabled = false });
        else
            AddFavoriteItems(menu.Items, _vm.Favorites);
    }

    /// <summary>お気に入り(フォルダ・グループ・区切り線)を、メニューの項目として並べる。グループは、サブメニューにする。</summary>
    private void AddFavoriteItems(ItemCollection target, IEnumerable<Favorite> favorites)
    {
        foreach (var fav in favorites.ToList())
        {
            var f = fav;
            if (f.IsSeparator)
            {
                target.Add(new Separator());
            }
            else if (f.IsGroup)
            {
                var group = new MenuItem { Header = FavoriteLabel(f) };
                if (f.Children.Count == 0) group.Items.Add(new MenuItem { Header = "(空)", IsEnabled = false });
                else AddFavoriteItems(group.Items, f.Children);
                target.Add(group);
            }
            else
            {
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
                target.Add(item);
            }
        }
    }

    /// <summary>お気に入りの整理ウィンドウを開く。「OK」で、整理したものに置き換える。</summary>
    private void ManageFavorites()
    {
        var window = new FavoritesWindow(_vm.Favorites) { Owner = this };
        if (window.ShowDialog() == true) _vm.ReplaceFavorites(window.Result);
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
        string text = PathSuggestPopup.IsOpen && PathSuggestList.SelectedItem is string chosen ? chosen : PathBox.Text; // 候補を矢印キーで選んでいれば、その候補へ
        ClosePathSuggest();
        if (!_vm.NavigateToText(text))
            MessageBox.Show("フォルダが見つかりません。", "KT_Explorer");
        e.Handled = true;
    }

    // ───────── パス欄: 入力補完・パンくず ─────────

    private bool _suppressSuggest;

    private void ClosePathSuggest() { _suggestTimer?.Stop(); PathSuggestPopup.IsOpen = false; PathSuggestList.ItemsSource = null; }

    /// <summary>入力補完の候補を、パス欄の入力に合わせて出す(入力していない、変更でない更新では出さない)。</summary>
    private void PathBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!PathBox.IsKeyboardFocusWithin) { UpdateBreadcrumb(); return; } // 入力中でなければ、フォルダの移動に合わせてパンくずを更新
        if (_suppressSuggest) return;
        // 打っている間は計算しない(速く打っても、入力を邪魔しない)。少し止まってから、候補を出す
        _suggestTimer ??= CreateSuggestTimer();
        _suggestTimer.Stop();
        _suggestTimer.Start();
    }

    private System.Windows.Threading.DispatcherTimer? _suggestTimer;

    private System.Windows.Threading.DispatcherTimer CreateSuggestTimer()
    {
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (!PathBox.IsKeyboardFocusWithin) return;
            var items = PathBarService.Suggest(PathBox.Text, 12);
            if (items.Count == 0) { ClosePathSuggest(); return; }
            PathSuggestList.ItemsSource = items;
            PathSuggestList.SelectedIndex = -1;
            PathSuggestList.Width = Math.Max(320, PathBox.ActualWidth);
            PathSuggestPopup.IsOpen = true;
        };
        return timer;
    }

    /// <summary>↓↑ で候補を選ぶ。Esc で候補を閉じる。Tab で、先頭(または選んでいる)候補を入力欄に入れる。</summary>
    private void PathBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!PathSuggestPopup.IsOpen) return;
        int count = PathSuggestList.Items.Count;
        if (e.Key == Key.Down) { PathSuggestList.SelectedIndex = Math.Min(count - 1, PathSuggestList.SelectedIndex + 1); PathSuggestList.ScrollIntoView(PathSuggestList.SelectedItem); e.Handled = true; }
        else if (e.Key == Key.Up) { PathSuggestList.SelectedIndex = Math.Max(0, PathSuggestList.SelectedIndex - 1); PathSuggestList.ScrollIntoView(PathSuggestList.SelectedItem); e.Handled = true; }
        else if (e.Key == Key.Escape) { ClosePathSuggest(); e.Handled = true; }
        else if (e.Key == Key.Tab && count > 0)
        {
            string pick = (PathSuggestList.SelectedItem as string) ?? (string)PathSuggestList.Items[0]!;
            _suppressSuggest = true;
            PathBox.Text = pick.EndsWith('\\') ? pick : pick + "\\"; // 次の階層の補完へ続けられるよう、区切りを付ける
            PathBox.CaretIndex = PathBox.Text.Length;
            _suppressSuggest = false;
            var next = PathBarService.Suggest(PathBox.Text, 12);
            PathSuggestList.ItemsSource = next; PathSuggestList.SelectedIndex = -1;
            PathSuggestPopup.IsOpen = next.Count > 0;
            e.Handled = true;
        }
    }

    private void PathSuggestList_Click(object sender, MouseButtonEventArgs e)
    {
        if ((e.OriginalSource as DependencyObject) is not DependencyObject src) return;
        var item = ItemsControl.ContainerFromElement(PathSuggestList, src) as ListBoxItem;
        if (item?.Content is not string path) return;
        ClosePathSuggest();
        if (!_vm.NavigateToText(path)) MessageBox.Show("フォルダが見つかりません。", "KT_Explorer");
    }

    private void PathBox_FocusChanged(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (!PathBox.IsKeyboardFocusWithin) ClosePathSuggest();
        UpdateBreadcrumb();
    }

    /// <summary>パンくずを、いまのフォルダに合わせて作り直す(入力中は隠す)。</summary>
    internal void UpdateBreadcrumb()
    {
        bool editing = PathBox.IsKeyboardFocusWithin;
        Breadcrumb.Visibility = editing ? Visibility.Collapsed : Visibility.Visible;
        BreadcrumbPanel.Children.Clear();
        string? path = _vm.CurrentFolder?.Path;
        if (editing || string.IsNullOrEmpty(path)) return;

        var segments = PathBarService.Split(path);
        for (int i = 0; i < segments.Count; i++)
        {
            var seg = segments[i];
            var crumb = new Button
            {
                Style = (Style)FindResource("KtFlatButton"), Height = 22,
                Content = new TextBlock { Text = seg.Name }, /* 文字列のままだと、名前の「_」がアクセスキー扱いで消える */ Padding = new Thickness(6, 0, 6, 0), Margin = new Thickness(0), MinWidth = 0,
                Background = Brushes.Transparent, BorderThickness = new Thickness(0), Focusable = false, ToolTip = seg.FullPath, Tag = seg.FullPath,
            };
            crumb.Click += (_, _) =>
            {
                if ((Keyboard.Modifiers & ModifierKeys.Control) != 0) _vm.OpenInNewTab(seg.FullPath);
                else _vm.NavigateToText(seg.FullPath);
            };
            BreadcrumbPanel.Children.Add(crumb);

            // ▸: その階層の中のフォルダを一覧にして、選んだものへ移動
            var arrow = new Button
            {
                Style = (Style)FindResource("KtFlatButton"), Height = 22,
                Content = ">", Padding = new Thickness(0), Margin = new Thickness(0), MinWidth = 0, Background = Brushes.Transparent, BorderThickness = new Thickness(0),
                Focusable = false, Foreground = Brushes.Gray, ToolTip = "この中のフォルダ", Tag = seg.FullPath,
            };
            arrow.Click += (_, _) => ShowCrumbChildren(arrow, seg.FullPath);
            BreadcrumbPanel.Children.Add(arrow);
        }
    }

    private void ShowCrumbChildren(Button anchor, string path)
    {
        var menu = new ContextMenu { PlacementTarget = anchor, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom, MaxHeight = 400 };
        foreach (var child in PathBarService.Children(path))
        {
            var c = child;
            var item = new MenuItem { Header = c.Name };
            item.Click += (_, _) =>
            {
                if ((Keyboard.Modifiers & ModifierKeys.Control) != 0) _vm.OpenInNewTab(c.FullPath);
                else _vm.NavigateToText(c.FullPath);
            };
            menu.Items.Add(item);
        }
        if (menu.Items.Count == 0) menu.Items.Add(new MenuItem { Header = "(フォルダはありません)", IsEnabled = false });
        menu.IsOpen = true;
    }

    // ───────── パスのコピー(パス欄が狭くて、全体を選べないとき用) ─────────

    private static void OpenMenuAt(ContextMenu menu, FrameworkElement target, ContextMenuEventArgs e)
    {
        // WPF に開かせると古いメニューが出ることがあるため、タブのメニューと同じく、自分で開く
        e.Handled = true;
        menu.PlacementTarget = target;
        menu.Placement = e.CursorLeft < 0
            ? System.Windows.Controls.Primitives.PlacementMode.Bottom
            : System.Windows.Controls.Primitives.PlacementMode.MousePoint;
        menu.IsOpen = true;
    }

    private static MenuItem CopyPathItem(string header, string text)
    {
        var item = new MenuItem { Header = header, IsEnabled = !string.IsNullOrEmpty(text) };
        item.Click += (_, _) => FolderActions.CopyPathText(new[] { text });
        return item;
    }

    /// <summary>パンくずの右クリック: 現在のフォルダのパス全体をコピー。階層の上なら、その階層までのパスもコピーできる。</summary>
    private void Breadcrumb_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        var menu = new ContextMenu();
        if ((e.OriginalSource as DependencyObject) is { } d && FindAncestor<Button>(d) is { Tag: string crumbPath } &&
            !string.Equals(crumbPath, _vm.CurrentFolder?.Path, StringComparison.OrdinalIgnoreCase))
            menu.Items.Add(CopyPathItem($"この階層のパスをコピー: {crumbPath}", crumbPath));
        menu.Items.Add(CopyPathItem("パスをコピー", _vm.CurrentFolder?.Path ?? ""));
        menu.Items.Add(new Separator());
        var edit = new MenuItem { Header = "パスを入力する", InputGestureText = "Ctrl+L" };
        edit.Click += (_, _) => { PathBox.Focus(); PathBox.SelectAll(); };
        menu.Items.Add(edit);
        OpenMenuAt(menu, Breadcrumb, e);
    }

    /// <summary>パス欄の右クリック: ふつうの編集メニューに、パス全体をコピーする項目を足す。</summary>
    private void PathBox_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        var menu = PathBox.ContextMenu!;   // XAML で持たせた空のメニュー。ここで中身を入れて、WPF に開かせる
        menu.Items.Clear();
        menu.Items.Add(CopyPathItem("パス全体をコピー", PathBox.Text));
        menu.Items.Add(new Separator());
        menu.Items.Add(new MenuItem { Header = "切り取り", Command = ApplicationCommands.Cut, CommandTarget = PathBox });
        menu.Items.Add(new MenuItem { Header = "コピー", Command = ApplicationCommands.Copy, CommandTarget = PathBox });
        menu.Items.Add(new MenuItem { Header = "貼り付け", Command = ApplicationCommands.Paste, CommandTarget = PathBox });
        menu.Items.Add(new MenuItem { Header = "すべて選択", Command = ApplicationCommands.SelectAll, CommandTarget = PathBox });
    }

    /// <summary>パンくずの空いているところをクリックすると、パスの入力に切り替える。</summary>
    private void Breadcrumb_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is Button || (e.OriginalSource as DependencyObject)?.GetType() == typeof(System.Windows.Controls.Primitives.ButtonBase)) return;
        PathBox.Focus(); PathBox.SelectAll();
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

    // グループタブをドラッグして、並べ替える
    private void GroupBar_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragGroup = (e.OriginalSource as DependencyObject) is { } d ? FindAncestor<ListBoxItem>(d)?.DataContext as TabGroupViewModel : null;
        _groupDragStart = e.GetPosition(null);
    }

    private void GroupBar_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragGroup == null) return;
        var delta = e.GetPosition(null) - _groupDragStart;
        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance) return;

        var group = _dragGroup;
        _dragGroup = null;
        DragDrop.DoDragDrop(GroupBar, new DataObject(GroupDragFormat, group), DragDropEffects.Move);
    }

    // グループの上へのドラッグ: タブ(このグループへ移動)か、グループ(この位置へ並べ替え)
    private void Group_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(TabDragFormat) || e.Data.GetDataPresent(GroupDragFormat) ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private void Group_Drop(object sender, DragEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is TabGroupViewModel group)
        {
            if (e.Data.GetData(TabDragFormat) is TabBase tab) _vm.MoveTabToGroup(tab, group);
            else if (e.Data.GetData(GroupDragFormat) is TabGroupViewModel dragged) _vm.ReorderGroup(dragged, group);
        }
        e.Handled = true;
    }

    // グループの右クリックメニュー(名前を変更 / グループの色 / グループを削除)。開くたびに組み立てる
    private void GroupItem_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not TabGroupViewModel group) return;
        var menu = new ContextMenu();
        var rename = new MenuItem { Header = "名前を変更" };
        rename.Click += (_, _) => RenameGroup(group);
        menu.Items.Add(rename);
        menu.Items.Add(BuildColorMenu("グループの色", group.Color, hex => _vm.SetGroupColor(group, hex)));
        menu.Items.Add(new Separator());
        var delete = new MenuItem { Header = "グループを削除" };
        delete.Click += (_, _) => DeleteGroup(group);
        menu.Items.Add(delete);
        OpenMenuAt(menu, fe, e);
    }

    /// <summary>色の見本つきのサブメニュー(「色なし」とパレット。いまの色にチェック)。タブとグループで共用。</summary>
    private static MenuItem BuildColorMenu(string header, string? current, Action<string?> apply)
    {
        var colorMenu = new MenuItem { Header = header };
        var none = new MenuItem { Header = "色なし", IsChecked = current == null };
        none.Click += (_, _) => apply(null);
        colorMenu.Items.Add(none);
        foreach (var (name, hex) in TabColors.Palette)
        {
            string h = hex;
            var item = new MenuItem
            {
                Header = name,
                IsChecked = string.Equals(current, h, StringComparison.OrdinalIgnoreCase),
                Icon = new System.Windows.Shapes.Rectangle { Width = 12, Height = 12, Fill = TabColors.Brush(h) },
            };
            item.Click += (_, _) => apply(h);
            colorMenu.Items.Add(item);
        }
        return colorMenu;
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
        string? tabPath = tab switch { FolderTabViewModel f => f.Path, PaneTabViewModel p => p.ActivePane?.Path, _ => null };
        menu.Items.Add(CopyPathItem("パスをコピー", tabPath ?? ""));
        menu.Items.Add(new Separator());

        // ピン留め(フォルダタブのみ): 留めたタブから別のフォルダへ移ると、移動先を新しいタブで開く
        bool pinned = tab.IsPinned;
        var pin = new MenuItem { Header = pinned ? "ピン留めを外す" : "タブをピン留め", IsEnabled = tab is FolderTabViewModel };
        pin.Click += (_, _) => _vm.TogglePin(tab);
        menu.Items.Add(pin);

        // タブの色: 色の見本つき。いまの色にチェック
        menu.Items.Add(BuildColorMenu("タブの色", tab.TabColor, hex => _vm.SetTabColor(tab, hex)));
        menu.Items.Add(new Separator());
        var close = new MenuItem { Header = "タブを閉じる", InputGestureText = "Ctrl+W", IsEnabled = !tab.IsPinned,
            ToolTip = tab.IsPinned ? "ピン留めされたタブは閉じられません(ピン留めを外してください)" : null };
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

    private void RenameGroup(TabGroupViewModel group)
    {
        var name = InputDialog.Ask(this, "グループ名を変更", "新しいグループ名:", group.Name);
        if (name != null) _vm.RenameGroup(group, name);
    }

    private void DeleteGroup(TabGroupViewModel group)
    {
        if (group.IsDefault)
        {
            MessageBox.Show("デフォルトグループは削除できません。", "KT_Explorer");
            return;
        }
        if (group.Tabs.Any(t => t.IsPinned))
        {
            MessageBox.Show("ピン留めされたタブがあるため、このグループは削除できません。\nピン留めを外すか、タブを別のグループへ移してください。", "KT_Explorer");
            return;
        }
        var r = MessageBox.Show($"グループ「{group.Name}」と、中の {group.Tabs.Count} 個のタブを閉じます。よろしいですか?",
            "KT_Explorer", MessageBoxButton.OKCancel, MessageBoxImage.Question);
        if (r == MessageBoxResult.OK) _vm.DeleteGroup(group);
    }

    // ───────── ペイン(指定モード) ─────────

    /// <summary>
    /// 「指定ペイン ▾」: 2 つか 3 つのタブ・フォルダを選び、「作成」を押して、ペインを作る。
    /// 既存タブから選択: タブにチェックを付けるダイアログ / フォルダを指定: フォルダを入力するダイアログ。
    /// </summary>
    private void SpecifyPane_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = (UIElement)sender, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };

        var candidates = _vm.AllFolderTabs().ToList();
        var fromTab = new MenuItem { Header = "既存タブから選択…", IsEnabled = candidates.Count >= PaneRequest.Min };
        fromTab.Click += (_, _) => Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, () =>
        {
            var list = candidates.Select(c => ($"{c.Group.Name} / {c.Tab.Title}  —  {c.Tab.Path}", c.Tab.Path)).ToList();
            var picked = PaneCreateDialog.PickTabs(this, list, _vm.CurrentFolder?.Path);
            if (picked != null && !_vm.CreatePanes(picked))
                MessageBox.Show("ペインを作れませんでした(フォルダが見つからないタブがあります)。", "KT_Explorer");
        });
        menu.Items.Add(fromTab);

        var browse = new MenuItem { Header = "フォルダを指定…" };
        browse.Click += (_, _) => Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, () =>
        {
            var picked = PaneCreateDialog.PickFolders(this);
            if (picked != null && !_vm.CreatePanes(picked))
                MessageBox.Show("ペインを作れませんでした(フォルダが見つかりません)。", "KT_Explorer");
        });
        menu.Items.Add(browse);

        menu.IsOpen = true;
    }

    // ───────── ツリー ─────────

    /// <summary>ツリーのフォルダを Ctrl+クリックすると、そのフォルダを新しいタブで開く(いまのタブは動かさない)。展開の矢印のクリックは、いままでどおり。</summary>
    private void FolderTree_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0 || e.OriginalSource is not DependencyObject d) return;
        if (FindAncestor<System.Windows.Controls.Primitives.ToggleButton>(d) != null) return; // 展開の矢印
        if (FindAncestor<TreeViewItem>(d)?.DataContext is not TreeNodeViewModel { IsPlaceholder: false } node) return;
        _vm.OpenInNewTab(node.FullPath);
        e.Handled = true; // ツリーの選択(=いまのタブの移動)はさせない
    }

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
        ScrollTreeTo(node); // 仮想化しているので、見えていないノードは、先に、画面の要素を作らせる(入力欄を出すため)
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
