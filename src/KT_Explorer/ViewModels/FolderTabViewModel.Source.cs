using System.ComponentModel;
using System.Windows.Media;
using KT_Explorer.Models;

namespace KT_Explorer.ViewModels;

/// <summary>
/// ペインの「参照元のグループ」: ペインが、どのグループから来たか。ペインの一覧・見出し・ヘッダーは、参照元のグループの色になる。
/// 通常のタブと、参照元の無いペイン(フォルダを指定して作ったもの)は null で、表示中のグループの色になる。
/// ペインは保存しないので、参照元も保存しない。
/// </summary>
public partial class FolderTabViewModel
{
    private static readonly Brush DefaultPaneBar = Frozen(Color.FromRgb(0xEF, 0xEF, 0xEF));
    private TabGroupViewModel? _sourceGroup;

    public TabGroupViewModel? SourceGroup
    {
        get => _sourceGroup;
        set
        {
            if (ReferenceEquals(_sourceGroup, value)) return;
            if (_sourceGroup != null) _sourceGroup.PropertyChanged -= OnSourceGroupChanged;
            _sourceGroup = value;
            if (_sourceGroup != null) _sourceGroup.PropertyChanged += OnSourceGroupChanged;
            NotifySource();
        }
    }

    private void OnSourceGroupChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(TabGroupViewModel.Color) or nameof(TabGroupViewModel.Name)) NotifySource();
    }

    private void NotifySource()
    {
        OnPropertyChanged(nameof(HasSource));
        OnPropertyChanged(nameof(SourceName));
        OnPropertyChanged(nameof(SourceViewBackground));
        OnPropertyChanged(nameof(SourceViewHeaderBackground));
        OnPropertyChanged(nameof(PaneBarBrush));
        OnPropertyChanged(nameof(SourceTagBrush));
        OnPropertyChanged(nameof(SourceTagTextBrush));
    }

    public bool HasSource => _sourceGroup != null;
    public string SourceName => _sourceGroup?.Name ?? "";

    /// <summary>参照元の色の一覧の背景。参照元が無ければ null(表示中のグループの色を使う)。</summary>
    public Brush? SourceViewBackground => _sourceGroup == null ? null : TabColors.Pale(_sourceGroup.Color, TabColors.ViewTint);

    /// <summary>参照元の色の列見出しの背景。参照元が無ければ null。参照元に色が無ければ透明。</summary>
    public Brush? SourceViewHeaderBackground => _sourceGroup == null ? null
        : _sourceGroup.Color == null ? Brushes.Transparent : TabColors.Pale(_sourceGroup.Color, TabColors.HeaderTint);

    /// <summary>ペインのヘッダー(パスの行)の背景。参照元に色があればその色、無ければ灰色。</summary>
    public Brush PaneBarBrush => _sourceGroup?.Color is { } c ? TabColors.Pale(c, TabColors.HeaderTint) : DefaultPaneBar;

    /// <summary>ヘッダーの名札(参照元のグループ名)の背景。</summary>
    public Brush SourceTagBrush => TabColors.Brush(_sourceGroup?.Color ?? TabColors.NoColorTag);

    /// <summary>名札の文字の色: 明るい色(黄など)の上では黒、それ以外は白。</summary>
    public Brush SourceTagTextBrush => TabColors.TextOn(_sourceGroup?.Color);

    private static Brush Frozen(Color color)
    {
        var b = new SolidColorBrush(color);
        b.Freeze();
        return b;
    }
}
