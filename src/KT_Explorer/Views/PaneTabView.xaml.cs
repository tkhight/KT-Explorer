using System.Windows;
using System.Windows.Controls;
using KT_Explorer.ViewModels;

namespace KT_Explorer.Views;

public partial class PaneTabView : UserControl
{
    public PaneTabView() => InitializeComponent();

    private void ClosePane_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is PaneTabViewModel tab && ((FrameworkElement)sender).DataContext is FolderTabViewModel pane)
            tab.RemovePane(pane);
    }
}
