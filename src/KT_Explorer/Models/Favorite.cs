using CommunityToolkit.Mvvm.ComponentModel;

namespace KT_Explorer.Models;

/// <summary>お気に入りフォルダ。</summary>
public partial class Favorite : ObservableObject
{
    [ObservableProperty] private string _name;
    [ObservableProperty] private string _path;

    public Favorite(string name, string path)
    {
        _name = name;
        _path = path;
    }
}
