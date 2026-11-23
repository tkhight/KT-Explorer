using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using KT_Explorer.ViewModels;

namespace KT_Explorer.Views;

/// <summary>ペインを横に並べる。ペインの間のスプリッターをドラッグして、幅を変えられる。</summary>
public partial class PaneTabView : UserControl
{
    private const double MinPaneWidth = 120;
    private const double SplitterWidth = 5;

    private PaneTabViewModel? _tab;

    public PaneTabView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Attach(DataContext as PaneTabViewModel);
        Unloaded += (_, _) => Attach(null);
        Loaded += (_, _) => { if (_tab == null) Attach(DataContext as PaneTabViewModel); };
    }

    private void Attach(PaneTabViewModel? tab)
    {
        if (ReferenceEquals(_tab, tab)) return;
        if (_tab != null) _tab.Panes.CollectionChanged -= OnPanesChanged;
        _tab = tab;
        if (_tab != null) _tab.Panes.CollectionChanged += OnPanesChanged;
        Rebuild();
    }

    private void OnPanesChanged(object? sender, NotifyCollectionChangedEventArgs e) => Rebuild();

    /// <summary>ペインの数に合わせて、列・ペイン・スプリッターを並べ直す(幅は等分に戻る)。</summary>
    private void Rebuild()
    {
        // ペインの中身を外してから組み直す(古い一覧を次の表示まで保持しない)
        foreach (var child in Host.Children.OfType<ContentPresenter>()) child.Content = null;
        Host.Children.Clear();
        Host.ColumnDefinitions.Clear();
        if (_tab == null) return;

        var template = (DataTemplate)Resources["PaneTemplate"];
        int column = 0;
        for (int i = 0; i < _tab.Panes.Count; i++)
        {
            if (i > 0)
            {
                Host.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var splitter = new GridSplitter
                {
                    Width = SplitterWidth,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Stretch,
                    ResizeDirection = GridResizeDirection.Columns,
                    ResizeBehavior = GridResizeBehavior.PreviousAndNext,
                    Background = System.Windows.Media.Brushes.Transparent,
                    Cursor = System.Windows.Input.Cursors.SizeWE,
                    ToolTip = "ドラッグして、ペインの幅を変える",
                };
                Grid.SetColumn(splitter, column++);
                Host.Children.Add(splitter);
            }

            Host.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = MinPaneWidth });
            var pane = new ContentPresenter { Content = _tab.Panes[i], ContentTemplate = template };
            Grid.SetColumn(pane, column++);
            Host.Children.Add(pane);
        }
    }

    private void ClosePane_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is PaneTabViewModel tab && ((FrameworkElement)sender).DataContext is FolderTabViewModel pane)
            tab.RemovePane(pane);
    }
}
