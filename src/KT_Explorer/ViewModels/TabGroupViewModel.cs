using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace KT_Explorer.ViewModels;

/// <summary>タブグループ。タブを束ねる単位(プロジェクトごとなど)。</summary>
public partial class TabGroupViewModel : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Display))]
    private string _name;

    public TabGroupViewModel(string name, bool isDefault = false)
    {
        _name = name;
        IsDefault = isDefault;
        Tabs.CollectionChanged += (_, _) => OnPropertyChanged(nameof(Display));
    }

    /// <summary>デフォルトグループ(外部から開いたフォルダの取り込み先。削除不可)。</summary>
    public bool IsDefault { get; }

    public ObservableCollection<TabBase> Tabs { get; } = new();

    /// <summary>このグループで最後に選択していたタブ(グループ切替時に復元)。</summary>
    public TabBase? LastSelected { get; set; }

    public string Display => $"{Name} ({Tabs.Count})";
}
