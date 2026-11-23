namespace KT_Explorer.Services;

/// <summary>
/// 計算したフォルダのサイズの記憶(アプリ全体で共有)。別のフォルダへ移って戻っても、計算し直さずに表示するために使う。
/// フォルダの中身が変わったら、そのフォルダと、それを含む上位のフォルダの記憶は捨てる
/// (ファイル操作・一覧の自動更新のとき)。それ以外の、アプリの外での変更は、一定時間(既定 5 分)で古いものとして捨てる。
/// </summary>
public static class FolderSizeCache
{
    /// <summary>記憶を使う期間。</summary>
    public static TimeSpan Lifetime { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>現在時刻(テストで差し替える)。</summary>
    public static Func<DateTime> Now { get; set; } = () => DateTime.UtcNow;

    private const int Capacity = 5000;
    private static readonly object Gate = new();
    private static readonly Dictionary<string, (long Size, DateTime At)> Map = new(StringComparer.OrdinalIgnoreCase);

    private static string Key(string path) => path.TrimEnd('\\', '/');

    public static bool TryGet(string path, out long size)
    {
        lock (Gate)
        {
            if (Map.TryGetValue(Key(path), out var entry))
            {
                if (Now() - entry.At <= Lifetime) { size = entry.Size; return true; }
                Map.Remove(Key(path)); // 古くなった
            }
        }
        size = 0;
        return false;
    }

    public static void Set(string path, long size)
    {
        lock (Gate)
        {
            if (Map.Count >= Capacity) RemoveOldest(Capacity / 4);
            Map[Key(path)] = (size, Now());
        }
    }

    /// <summary>path と、それを含む上位のフォルダの記憶を捨てる(path の直下に、ファイルやフォルダが増減・変更したとき)。</summary>
    public static void InvalidateUp(string path)
    {
        string key = Key(path);
        lock (Gate)
        {
            foreach (var k in Map.Keys.Where(k => IsSameOrAncestor(k, key)).ToList()) Map.Remove(k);
        }
    }

    /// <summary>path・上位のフォルダ・path の中のフォルダの記憶を捨てる(path が削除・移動・名前変更されたとき)。</summary>
    public static void InvalidateTree(string path)
    {
        string key = Key(path);
        lock (Gate)
        {
            foreach (var k in Map.Keys.Where(k => IsSameOrAncestor(k, key) || IsSameOrAncestor(key, k)).ToList()) Map.Remove(k);
        }
    }

    public static void Clear()
    {
        lock (Gate) Map.Clear();
    }

    /// <summary>ancestor が、path と同じか、path を含む上位のフォルダか。</summary>
    private static bool IsSameOrAncestor(string ancestor, string path) =>
        string.Equals(ancestor, path, StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith(ancestor + "\\", StringComparison.OrdinalIgnoreCase);

    private static void RemoveOldest(int count)
    {
        foreach (var k in Map.OrderBy(p => p.Value.At).Take(count).Select(p => p.Key).ToList()) Map.Remove(k);
    }
}
