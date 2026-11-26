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
    [NotifyPropertyChangedFor(nameof(ColorBrush))]
    [NotifyPropertyChangedFor(nameof(TintBrush))]
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

    /// <summary>グループの下の線の色(色なしは透明)。</summary>
    public Brush ColorBrush => TabColors.Brush(Color);

    /// <summary>グループの背景の薄い色(色なしは透明)。</summary>
    public Brush TintBrush => TabColors.Brush(Color, 60);
}
