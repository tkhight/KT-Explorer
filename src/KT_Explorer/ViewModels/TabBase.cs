using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using KT_Explorer.Models;

namespace KT_Explorer.ViewModels;

/// <summary>タブバーに並ぶ項目の共通基底(通常のフォルダタブ / ペインタブ)。</summary>
public abstract partial class TabBase : ObservableObject, IDisposable
{
    [ObservableProperty] private string _title = "";

    /// <summary>ピン留め。ピン留めされたフォルダタブは、そこから別のフォルダへ移動すると、移動先を新しいタブで開く(ペインタブは対象外)。</summary>
    [ObservableProperty] private bool _isPinned;

    /// <summary>タブの色(<see cref="TabColors.Palette"/> の #RRGGBB)。色なしは null。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TabColorBrush))]
    [NotifyPropertyChangedFor(nameof(TabTintBrush))]
    private string? _tabColor;

    /// <summary>タブの下の線の色(色なしは透明)。</summary>
    public Brush TabColorBrush => TabColors.Brush(TabColor);

    /// <summary>タブの背景の薄い色(色なしは透明)。</summary>
    public Brush TabTintBrush => TabColors.Brush(TabColor, 60);

    /// <summary>タブを閉じるとき、ファイル監視などの資源を解放する。</summary>
    public virtual void Dispose() { }

    /// <summary>タブにホバーしたときに出す説明(完全なパスなど)。</summary>
    public virtual string ToolTipText => Title;
}
