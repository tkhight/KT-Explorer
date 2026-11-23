using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace KT_Explorer.Models;

public enum FavoriteKind
{
    /// <summary>フォルダへのお気に入り。</summary>
    Folder,

    /// <summary>お気に入りをまとめるグループ(メニューでは、サブメニューになる)。</summary>
    Group,

    /// <summary>メニューの区切り線。</summary>
    Separator,
}

/// <summary>お気に入り(フォルダ・グループ・区切り線)。グループは、中にフォルダと区切り線を持つ(入れ子にはしない)。</summary>
public partial class Favorite : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayText))]
    private string _name;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayText))]
    private string _path;

    /// <summary>整理ウィンドウで選択中か(並べ替えのあとも選択を保つために使う)。</summary>
    [ObservableProperty] private bool _isSelected;

    /// <summary>整理ウィンドウで、グループを開いているか。</summary>
    [ObservableProperty] private bool _isExpanded = true;

    public FavoriteKind Kind { get; }

    /// <summary>グループの中身(グループ以外では常に空)。</summary>
    public ObservableCollection<Favorite> Children { get; } = new();

    public Favorite(string name, string path) : this(FavoriteKind.Folder, name, path)
    {
    }

    private Favorite(FavoriteKind kind, string name, string path)
    {
        Kind = kind;
        _name = name;
        _path = path;
    }

    public static Favorite NewGroup(string name) => new(FavoriteKind.Group, name, "");

    public static Favorite NewSeparator() => new(FavoriteKind.Separator, "", "");

    public bool IsFolder => Kind == FavoriteKind.Folder;
    public bool IsGroup => Kind == FavoriteKind.Group;
    public bool IsSeparator => Kind == FavoriteKind.Separator;

    /// <summary>整理ウィンドウでの表示。</summary>
    public string DisplayText => Kind switch
    {
        FavoriteKind.Group => $"[グループ] {Name}",
        FavoriteKind.Separator => "────────  区切り線  ────────",
        _ => string.IsNullOrEmpty(Path) ? Name : $"{Name}    ({Path})",
    };

    /// <summary>グループの中身も含めて、複製する(整理ウィンドウでは、複製を編集して、OK で反映する)。</summary>
    public Favorite Clone()
    {
        var copy = new Favorite(Kind, Name, Path) { IsExpanded = IsExpanded };
        foreach (var child in Children) copy.Children.Add(child.Clone());
        return copy;
    }
}
