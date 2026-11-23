using System.Collections.ObjectModel;
using KT_Explorer.Models;

namespace KT_Explorer.Services;

/// <summary>お気に入り(トップの並びと、グループの中身)の並べ替え・移動などの操作。</summary>
public static class FavoriteTree
{
    /// <summary>favorite を含んでいる並び(トップ、または、グループの中身)。無ければ null。</summary>
    public static ObservableCollection<Favorite>? ParentList(ObservableCollection<Favorite> root, Favorite favorite)
    {
        if (root.Contains(favorite)) return root;
        foreach (var group in root.Where(f => f.IsGroup))
            if (group.Children.Contains(favorite)) return group.Children;
        return null;
    }

    /// <summary>すべてのフォルダのお気に入り(グループの中のものも含む)。</summary>
    public static IEnumerable<Favorite> Folders(IEnumerable<Favorite> root)
    {
        foreach (var f in root)
        {
            if (f.IsFolder) yield return f;
            else if (f.IsGroup)
                foreach (var child in Folders(f.Children)) yield return child;
        }
    }

    public static ObservableCollection<Favorite> Clone(IEnumerable<Favorite> root)
    {
        var copy = new ObservableCollection<Favorite>();
        foreach (var f in root) copy.Add(f.Clone());
        return copy;
    }

    /// <summary>同じ並びの中で、1つ上へ。できれば true。</summary>
    public static bool MoveUp(ObservableCollection<Favorite> root, Favorite favorite)
    {
        var list = ParentList(root, favorite);
        if (list == null) return false;
        int i = list.IndexOf(favorite);
        if (i <= 0) return false;
        list.Move(i, i - 1);
        return true;
    }

    /// <summary>同じ並びの中で、1つ下へ。できれば true。</summary>
    public static bool MoveDown(ObservableCollection<Favorite> root, Favorite favorite)
    {
        var list = ParentList(root, favorite);
        if (list == null) return false;
        int i = list.IndexOf(favorite);
        if (i < 0 || i >= list.Count - 1) return false;
        list.Move(i, i + 1);
        return true;
    }

    /// <summary>
    /// グループの中(group)、または、トップの末尾(group が null)へ移す。グループ自体は、グループの中へは入れられない
    /// (入れ子にしない)。移せれば true。
    /// </summary>
    public static bool MoveTo(ObservableCollection<Favorite> root, Favorite favorite, Favorite? group)
    {
        if (group != null && (!group.IsGroup || favorite.IsGroup || ReferenceEquals(group, favorite))) return false;
        var from = ParentList(root, favorite);
        if (from == null) return false;
        var to = group == null ? root : group.Children;
        if (ReferenceEquals(from, to)) return false;
        from.Remove(favorite);
        to.Add(favorite);
        return true;
    }

    /// <summary>favorite を取り除く(グループなら、中身ごと)。</summary>
    public static bool Remove(ObservableCollection<Favorite> root, Favorite favorite)
    {
        var list = ParentList(root, favorite);
        return list != null && list.Remove(favorite);
    }

    /// <summary>
    /// 区切り線を、favorite の直後に入れる(favorite が null なら、トップの末尾)。
    /// グループの中の項目の直後なら、グループの中に入る。
    /// </summary>
    public static Favorite InsertSeparatorAfter(ObservableCollection<Favorite> root, Favorite? favorite)
    {
        var separator = Favorite.NewSeparator();
        var list = favorite == null ? root : ParentList(root, favorite) ?? root;
        int index = favorite == null ? list.Count : list.IndexOf(favorite) + 1;
        list.Insert(Math.Clamp(index, 0, list.Count), separator);
        return separator;
    }
}
