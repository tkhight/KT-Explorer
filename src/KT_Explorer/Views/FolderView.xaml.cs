using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using KT_Explorer.Models;
using KT_Explorer.ViewModels;

namespace KT_Explorer.Views;

/// <summary>1つのフォルダの一覧表示。通常のタブとペインタブの各ペインで共用する。</summary>
public partial class FolderView : UserControl
{
    public FolderView() => InitializeComponent();

    private FolderTabViewModel? Vm => DataContext as FolderTabViewModel;

    // ペイン内で操作を始めたら、そのペインを操作対象にする
    private void OnActivate(object sender, RoutedEventArgs e) => Vm?.RequestActivate();

    private void FileList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject d && FindAncestor<ListViewItem>(d) != null
            && FileList.SelectedItem is FileEntry entry)
            Vm?.Open(entry);
    }

    private void FileList_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && FileList.SelectedItem is FileEntry entry) { Vm?.Open(entry); e.Handled = true; }
        else if (e.Key == Key.Back) { Vm?.GoUp(); e.Handled = true; }
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
