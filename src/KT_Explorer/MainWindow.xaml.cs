using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
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
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        var s = _vm.ToState();
        var b = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        s.Left = b.Left; s.Top = b.Top; s.Width = b.Width; s.Height = b.Height;
        s.Maximized = WindowState == WindowState.Maximized;
        StateService.Save(s);
        _desktop?.Dispose();
        base.OnClosing(e);
    }

    // ───────── マウス / キー ─────────

    // マウスのサイドボタン: 戻る / 進む
    private void Window_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.XButton1) { _vm.BackCommand.Execute(null); e.Handled = true; }
        else if (e.ChangedButton == MouseButton.XButton2) { _vm.ForwardCommand.Execute(null); e.Handled = true; }
    }

    // Ctrl+Tab / Ctrl+Shift+Tab: タブ切替(WPF 標準のフォーカス移動より先に処理する)
    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Tab && (Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            _vm.CycleTab((Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? -1 : 1);
            e.Handled = true;
        }
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
        menu.Items.Add(new Separator());
        var close = new MenuItem { Header = "タブを閉じる" };
        close.Click += (_, _) => _vm.CloseTabCommand.Execute(tab);
        menu.Items.Add(close);
        fe.ContextMenu = menu;
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

    private void TreeAddTab_Click(object sender, RoutedEventArgs e)
    {
        if (_contextNode != null) _vm.OpenInNewTab(_contextNode.FullPath);
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
