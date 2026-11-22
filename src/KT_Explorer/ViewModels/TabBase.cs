using CommunityToolkit.Mvvm.ComponentModel;

namespace KT_Explorer.ViewModels;

/// <summary>タブバーに並ぶ項目の共通基底(通常のフォルダタブ / ペインタブ)。</summary>
public abstract partial class TabBase : ObservableObject, IDisposable
{
    [ObservableProperty] private string _title = "";

    /// <summary>タブを閉じるとき、ファイル監視などの資源を解放する。</summary>
    public virtual void Dispose() { }

    /// <summary>タブにホバーしたときに出す説明(完全なパスなど)。</summary>
    public virtual string ToolTipText => Title;
}
