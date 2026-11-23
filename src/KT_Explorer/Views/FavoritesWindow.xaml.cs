using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using KT_Explorer.Models;
using KT_Explorer.Services;
using KT_Explorer.ViewModels;

namespace KT_Explorer.Views;

/// <summary>
/// お気に入りの整理ウィンドウ。お気に入りの複製を編集し、「OK」で、元に反映する(キャンセルなら、何も変わらない)。
/// 並べ替え・グループへの移動・区切り線・グループの追加・編集・削除ができる。
/// </summary>
public partial class FavoritesWindow : Window
{
    private readonly ObservableCollection<Favorite> _root;

    public FavoritesWindow(IEnumerable<Favorite> source)
    {
        InitializeComponent();
        _root = FavoriteTree.Clone(source);
        Tree.ItemsSource = _root;
        UpdateButtons();
    }

    /// <summary>「OK」で確定した、整理後のお気に入り。</summary>
    public ObservableCollection<Favorite> Result => _root;

    private Favorite? Selected => Tree.SelectedItem as Favorite;

    // ───────── ボタンの有効/無効 ─────────

    private void Tree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e) => UpdateButtons();

    private void UpdateButtons()
    {
        var f = Selected;
        bool has = f != null;
        var list = has ? FavoriteTree.ParentList(_root, f!) : null;
        int index = list != null ? list.IndexOf(f!) : -1;
        UpButton.IsEnabled = has && index > 0;
        DownButton.IsEnabled = has && list != null && index < list.Count - 1;
        MoveButton.IsEnabled = has && !f!.IsGroup;
        EditButton.IsEnabled = has && !f!.IsSeparator;
        DeleteButton.IsEnabled = has;
    }

    /// <summary>並べ替えなどで項目が作り直されても、選択を保つ。</summary>
    private void Reselect(Favorite favorite) =>
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            favorite.IsSelected = true;
            UpdateButtons();
        });

    // ───────── 操作 ─────────

    private void Up_Click(object sender, RoutedEventArgs e)
    {
        var f = Selected;
        if (f != null && FavoriteTree.MoveUp(_root, f)) Reselect(f);
    }

    private void Down_Click(object sender, RoutedEventArgs e)
    {
        var f = Selected;
        if (f != null && FavoriteTree.MoveDown(_root, f)) Reselect(f);
    }

    private void Move_Click(object sender, RoutedEventArgs e)
    {
        var f = Selected;
        if (f == null || f.IsGroup) return;

        var menu = new ContextMenu { PlacementTarget = MoveButton, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        var top = new MenuItem { Header = "(グループの外 = 一番上の階層)" };
        top.Click += (_, _) => { if (FavoriteTree.MoveTo(_root, f, null)) Reselect(f); };
        menu.Items.Add(top);
        foreach (var group in _root.Where(g => g.IsGroup).ToList())
        {
            var target = group;
            var item = new MenuItem { Header = new TextBlock { Text = $"グループ「{group.Name}」の中へ" } };
            item.Click += (_, _) => { if (FavoriteTree.MoveTo(_root, f, target)) { target.IsExpanded = true; Reselect(f); } };
            menu.Items.Add(item);
        }
        if (_root.All(g => !g.IsGroup)) menu.Items.Add(new MenuItem { Header = "(グループがありません。先に「グループを追加…」)", IsEnabled = false });
        menu.IsOpen = true;
    }

    private void Edit_Click(object sender, RoutedEventArgs e) => EditSelected();

    private void Tree_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (Selected is { IsSeparator: false } && e.OriginalSource is DependencyObject) EditSelected();
    }

    private void EditSelected()
    {
        var f = Selected;
        if (f == null || f.IsSeparator) return;

        var name = InputDialog.Ask(this, f.IsGroup ? "グループ名の変更" : "お気に入りを編集", "表示名:", f.Name);
        if (name == null) return;
        if (f.IsGroup)
        {
            f.Name = name;
            return;
        }

        var path = InputDialog.Ask(this, "お気に入りを編集", "フォルダのパス:", f.Path);
        if (path == null) return;
        f.Name = name;
        if (!Directory.Exists(path))
        {
            MessageBox.Show("フォルダが見つからないため、パスは変更しませんでした。", "KT_Explorer");
            return;
        }
        f.Path = FolderTabViewModel.Normalize(path);
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        var f = Selected;
        if (f == null) return;
        if (f.IsGroup && f.Children.Count > 0 &&
            MessageBox.Show($"グループ「{f.Name}」を、中の {f.Children.Count} 件ごと削除しますか?", "KT_Explorer",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        FavoriteTree.Remove(_root, f);
        UpdateButtons();
    }

    private void AddSeparator_Click(object sender, RoutedEventArgs e)
    {
        var separator = FavoriteTree.InsertSeparatorAfter(_root, Selected);
        Reselect(separator);
    }

    private void AddGroup_Click(object sender, RoutedEventArgs e)
    {
        var name = InputDialog.Ask(this, "グループを追加", "グループ名:", "新しいグループ");
        if (name == null) return;
        var group = Favorite.NewGroup(name);
        _root.Add(group);
        Reselect(group);
    }

    private void Ok_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
