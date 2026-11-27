using System.Collections.ObjectModel;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using KT_Explorer.Models;

namespace KT_Explorer.ViewModels;

/// <summary>タブグループ。タブを束ねる単位(プロジェクトごとなど)。</summary>
public partial class TabGroupViewModel : ObservableObject
{
    [ObservableProperty] private string _name;

    /// <summary>グループの色(<see cref="TabColors.Palette"/> の #RRGGBB)。色なしは null。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OutlineBrush))]
    [NotifyPropertyChangedFor(nameof(SelectedOutlineBrush))]
    [NotifyPropertyChangedFor(nameof(SelectedFillBrush))]
    private string? _color;

    public TabGroupViewModel(string name, bool isDefault = false)
    {
        _name = name;
        IsDefault = isDefault;
    }

    /// <summary>デフォルトグループ(外部から開いたフォルダの取り込み先。削除不可)。並べ替えで位置が変わっても、このグループのまま。</summary>
    public bool IsDefault { get; }

    public ObservableCollection<TabBase> Tabs { get; } = new();

    /// <summary>このグループで最後に選択していたタブ(グループ切替時に復元)。</summary>
    public TabBase? LastSelected { get; set; }

    // グループタブは、全周を囲む形(下の線で色を示すフォルダタブと、見分けられるように)

    /// <summary>選択していないときの枠の色(色なしは灰色)。</summary>
    public Brush OutlineBrush => TabColors.Brush(Color ?? "#C3C9D1");

    /// <summary>選択中の枠の色(色なしは青)。</summary>
    public Brush SelectedOutlineBrush => TabColors.Brush(Color ?? "#3B82D6");

    /// <summary>選択中の背景(グループの色を薄くしたもの。色なしは薄い青)。</summary>
    public Brush SelectedFillBrush => Color == null ? TabColors.Brush("#DCEBFA") : TabColors.Pale(Color, TabColors.HeaderTint);
}
