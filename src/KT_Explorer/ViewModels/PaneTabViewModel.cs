using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace KT_Explorer.ViewModels;

/// <summary>
/// ペインタブ(特殊タブ)。2〜3個のフォルダ表示を横に並べる一時的なタブ。
/// 各ペインはタブを持たず、状態は保存しない。
/// </summary>
public partial class PaneTabViewModel : TabBase
{
    public const int MaxPanes = 3;

    [ObservableProperty] private FolderTabViewModel? _activePane;

    public PaneTabViewModel(IEnumerable<string> paths) : this(paths, null) { }

    /// <param name="sources">各ペインの参照元のグループ(paths と同じ順)。null なら、参照元なし。</param>
    public PaneTabViewModel(IEnumerable<string> paths, IReadOnlyList<TabGroupViewModel?>? sources)
    {
        int i = 0;
        foreach (var p in paths)
        {
            AddPane(p, sources != null && i < sources.Count ? sources[i] : null);
            i++;
        }
    }

    public ObservableCollection<FolderTabViewModel> Panes { get; } = new();

    /// <summary>操作対象のペインが変わった(ツールバー/パス欄の対象切替用)。</summary>
    public event Action? ActivePaneChanged;

    /// <summary>ペインが1つ以下になり、タブごと閉じる必要がある。</summary>
    public event Action<PaneTabViewModel>? RequestClose;

    public override bool IsPane => true;

    public override string ToolTipText => string.Join("\n", Panes.Select(p => p.Path));

    public bool AddPane(string path, TabGroupViewModel? source = null)
    {
        if (Panes.Count >= MaxPanes) return false;
        var pane = new FolderTabViewModel(path) { SourceGroup = source };
        pane.Activated += p => ActivePane = p;
        pane.PathChanged += _ => UpdateTitle();
        Panes.Add(pane);
        ActivePane = pane; // 追加した右側のペインを操作対象にする
        UpdateTitle();
        return true;
    }

    public override void Dispose()
    {
        foreach (var pane in Panes) pane.Dispose();
    }

    public void RemovePane(FolderTabViewModel pane)
    {
        if (!Panes.Remove(pane)) return;
        pane.Dispose();
        if (ActivePane == pane) ActivePane = Panes.LastOrDefault();
        UpdateTitle();
        if (Panes.Count < 2) RequestClose?.Invoke(this);
    }

    partial void OnActivePaneChanged(FolderTabViewModel? oldValue, FolderTabViewModel? newValue)
    {
        if (oldValue != null) oldValue.IsActivePane = false;
        if (newValue != null) newValue.IsActivePane = true;
        ActivePaneChanged?.Invoke();
    }

    private void UpdateTitle()
    {
        Title = string.Join(" │ ", Panes.Select(p => p.Title));
        OnPropertyChanged(nameof(ToolTipText));
    }
}
