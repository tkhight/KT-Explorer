using System.Windows;
using System.Windows.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using KT_Explorer.Services;

namespace KT_Explorer.Views;

/// <summary>タブの候補1つ分(チェックの状態を持つ)。</summary>
public sealed partial class PaneChoice : ObservableObject
{
    public PaneChoice(string label, string path, bool isChecked)
    {
        Label = label;
        Path = path;
        _isChecked = isChecked;
    }

    public string Label { get; }
    public string Path { get; }

    [ObservableProperty] private bool _isChecked;

    /// <summary>チェックできるか(3 つ選んだあとは、残りは外すまでチェックできない)。</summary>
    [ObservableProperty] private bool _canCheck = true;
}

/// <summary>
/// 指定ペインの作成ダイアログ。2 つの使い方がある。
/// 「既存タブから選択」: タブにチェックを付け、2 つか 3 つ選んだ状態で「作成」を押す。
/// 「フォルダを指定」: フォルダを 2 つか 3 つ入力(または参照)した状態で「作成」を押す。
/// </summary>
public partial class PaneCreateDialog : Window
{
    private readonly List<PaneChoice> _choices = new();
    private readonly List<TextBox> _folderBoxes = new();
    private bool _tabMode;

    private PaneCreateDialog() => InitializeComponent();

    /// <summary>結果(作成するペインのフォルダ。2〜3 件)。</summary>
    public List<string> Result { get; private set; } = new();

    /// <summary>既存タブから選ぶ。candidates: (表示名, パス)。preChecked: 最初からチェックを付けるパス。キャンセルなら null。</summary>
    public static List<string>? PickTabs(Window owner, IReadOnlyList<(string Label, string Path)> candidates, string? preChecked)
    {
        var dlg = new PaneCreateDialog { Owner = owner, _tabMode = true, Title = "指定ペイン: 既存タブから選択" };
        dlg.HeaderText.Text = $"ペインにするタブにチェックを付けてください({PaneRequest.Min} つか {PaneRequest.Max} つ)。";
        foreach (var (label, path) in candidates)
            dlg._choices.Add(new PaneChoice(label, path, string.Equals(path, preChecked, StringComparison.OrdinalIgnoreCase)));
        dlg.TabList.ItemsSource = dlg._choices;
        dlg.TabList.Visibility = Visibility.Visible;
        dlg.Recompute();
        return dlg.ShowDialog() == true ? dlg.Result : null;
    }

    /// <summary>フォルダを 2 つか 3 つ指定する。キャンセルなら null。</summary>
    public static List<string>? PickFolders(Window owner)
    {
        var dlg = new PaneCreateDialog { Owner = owner, _tabMode = false, Title = "指定ペイン: フォルダを指定" };
        dlg.HeaderText.Text = $"ペインに表示するフォルダを、{PaneRequest.Min} つか {PaneRequest.Max} つ指定してください(パスを入力するか、「参照…」で選びます)。";
        for (int i = 0; i < PaneRequest.Max; i++) dlg.AddFolderRow(i + 1);
        dlg.FolderRows.Visibility = Visibility.Visible;
        dlg.Recompute();
        dlg.Loaded += (_, _) => dlg._folderBoxes[0].Focus();
        return dlg.ShowDialog() == true ? dlg.Result : null;
    }

    private void AddFolderRow(int number)
    {
        var grid = new Grid { Margin = new Thickness(0, 2, 0, 2) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var label = new TextBlock { Text = number + ":", VerticalAlignment = VerticalAlignment.Center };
        var box = new TextBox { VerticalContentAlignment = VerticalAlignment.Center, Padding = new Thickness(2) };
        System.Windows.Automation.AutomationProperties.SetAutomationId(box, "PaneFolder" + number);
        box.TextChanged += (_, _) => Recompute();
        var browse = new Button { Content = "参照…", Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(8, 0, 8, 0) };
        System.Windows.Automation.AutomationProperties.SetAutomationId(browse, "PaneBrowse" + number);
        browse.Click += (_, _) =>
        {
            var dlg = new Microsoft.Win32.OpenFolderDialog { Title = $"ペイン {number} に表示するフォルダ" };
            if (dlg.ShowDialog(this) == true) box.Text = dlg.FolderName;
        };

        Grid.SetColumn(label, 0); Grid.SetColumn(box, 1); Grid.SetColumn(browse, 2);
        grid.Children.Add(label); grid.Children.Add(box); grid.Children.Add(browse);
        FolderRows.Children.Add(grid);
        _folderBoxes.Add(box);
    }

    private void Choice_Changed(object sender, RoutedEventArgs e) => Recompute();

    /// <summary>入力の状態を見て、「作成」を押せるかと、案内の文を更新する。</summary>
    private void Recompute()
    {
        if (_tabMode)
        {
            int n = _choices.Count(c => c.IsChecked);
            foreach (var c in _choices) c.CanCheck = c.IsChecked || n < PaneRequest.Max; // 3 つ選んだら、残りは外すまでチェックできない
            Result = _choices.Where(c => c.IsChecked).Select(c => c.Path).ToList();
            CreateButton.IsEnabled = PaneRequest.IsValidCount(n);
            CountText.Text = $"選択: {n} 個({PaneRequest.Min}〜{PaneRequest.Max} 個で作成できます)";
        }
        else
        {
            var texts = _folderBoxes.Select(b => b.Text).ToList();
            var (paths, invalid) = PaneRequest.CleanFolders(texts);
            for (int i = 0; i < _folderBoxes.Count; i++)
                _folderBoxes[i].Background = invalid.Contains(i) ? System.Windows.Media.Brushes.MistyRose : System.Windows.Media.Brushes.White;
            Result = paths;
            CreateButton.IsEnabled = invalid.Count == 0 && PaneRequest.IsValidCount(paths.Count);
            CountText.Text = invalid.Count > 0
                ? "見つからないフォルダがあります(赤い欄)。"
                : $"指定: {paths.Count} 個({PaneRequest.Min}〜{PaneRequest.Max} 個で作成できます)";
        }
    }

    private void Create_Click(object sender, RoutedEventArgs e)
    {
        if (!CreateButton.IsEnabled) return;
        DialogResult = true;
    }
}
