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
    private readonly List<(ColumnDefinition Column, FolderTabViewModel Pane)> _paneColumns = new();

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

    /// <summary>
    /// ペインの数に合わせて、列・ペイン・スプリッターを並べ直す。ペインごとの幅の比率は保つ
    /// (新しく増えたペインは、既存のペインの平均の幅)。
    /// </summary>
    private void Rebuild()
    {
        // ペインの中身を外してから組み直す(古い一覧を次の表示まで保持しない)
        foreach (var child in Host.Children.OfType<ContentPresenter>()) child.Content = null;
        Host.Children.Clear();
        Host.ColumnDefinitions.Clear();
        _paneColumns.Clear();
        if (_tab == null) return;

        // 幅の比率が未設定(新しいペイン)は、設定済みのペインの平均にする
        var known = _tab.Panes.Where(p => p.PaneWeight > 0).Select(p => p.PaneWeight).ToList();
        double average = known.Count > 0 ? known.Average() : 1;
        foreach (var pane in _tab.Panes.Where(p => p.PaneWeight <= 0)) pane.PaneWeight = average;

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
                splitter.DragCompleted += (_, _) => SaveWidths();
                Grid.SetColumn(splitter, column++);
                Host.Children.Add(splitter);
            }

            var paneColumn = new ColumnDefinition { Width = new GridLength(_tab.Panes[i].PaneWeight, GridUnitType.Star), MinWidth = MinPaneWidth };
            Host.ColumnDefinitions.Add(paneColumn);
            _paneColumns.Add((paneColumn, _tab.Panes[i]));
            var pane = new ContentPresenter { Content = _tab.Panes[i], ContentTemplate = template };
            Grid.SetColumn(pane, column++);
            Host.Children.Add(pane);
        }
    }

    /// <summary>スプリッターのドラッグが終わったら、各ペインの幅を、比率として覚える(ペインの増減のあとも保つ)。</summary>
    private void SaveWidths()
    {
        foreach (var (column, pane) in _paneColumns)
            if (column.ActualWidth > 0) pane.PaneWeight = column.ActualWidth;
    }

    /// <summary>ペインの枠・ヘッダーのクリックでも、そのペインを操作中にする(一覧の外なので、一覧側の処理は働かない)。</summary>
    private void Pane_PreviewMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is FolderTabViewModel pane) pane.RequestActivate();
    }

    private void ClosePane_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is PaneTabViewModel tab && ((FrameworkElement)sender).DataContext is FolderTabViewModel pane)
            tab.RemovePane(pane);
    }
}
