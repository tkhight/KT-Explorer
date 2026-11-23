namespace KT_Explorer.Services;

/// <summary>開いたフォルダの履歴の1件(最後に開いた日時と、開いた回数)。</summary>
public sealed record FolderVisit(string Path, int Count, DateTime LastVisited);

/// <summary>
/// 開いたフォルダの履歴(アプリ全体で共有)。「最近開いたフォルダ」と「よく開くフォルダ」の表示や、
/// パス欄の入力補完に使う。状態ファイルに保存する(最大 300 件。古いものから捨てる)。
/// </summary>
public static class FolderHistory
{
    private const int MaxEntries = 300;
    private static readonly object Gate = new();
    private static readonly Dictionary<string, FolderVisit> Visits = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>現在時刻(テストで差し替える)。</summary>
    public static Func<DateTime> Now { get; set; } = () => DateTime.Now;

    /// <summary>履歴が変わった。</summary>
    public static event Action? Changed;

    private static string Key(string path)
    {
        string trimmed = path.TrimEnd('\\', '/');
        return trimmed.Length == 2 && trimmed[1] == ':' ? trimmed + "\\" : trimmed; // ドライブのルート(C:\)は、末尾の \ を残す
    }

    /// <summary>フォルダを開いた。回数を増やし、最後に開いた日時を更新する。</summary>
    public static void Record(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        string key = Key(path);
        lock (Gate)
        {
            Visits.TryGetValue(key, out var old);
            Visits[key] = new FolderVisit(key, (old?.Count ?? 0) + 1, Now());
            if (Visits.Count > MaxEntries)
            {
                foreach (var drop in Visits.Values.OrderBy(v => v.LastVisited).Take(Visits.Count - MaxEntries).Select(v => v.Path).ToList())
                    Visits.Remove(drop);
            }
        }
        Changed?.Invoke();
    }

    /// <summary>最近開いたフォルダ(新しい順)。</summary>
    public static IReadOnlyList<FolderVisit> Recent(int count)
    {
        lock (Gate) return Visits.Values.OrderByDescending(v => v.LastVisited).Take(count).ToList();
    }

    /// <summary>よく開くフォルダ(開いた回数の多い順。同じ回数なら新しい順)。minCount 回未満のものは出さない。</summary>
    public static IReadOnlyList<FolderVisit> Frequent(int count, int minCount = 2)
    {
        lock (Gate)
            return Visits.Values.Where(v => v.Count >= minCount)
                .OrderByDescending(v => v.Count).ThenByDescending(v => v.LastVisited).Take(count).ToList();
    }

    /// <summary>履歴のうち、パスに text を含むもの(パス欄の入力補完用。最近開いた順)。</summary>
    public static IReadOnlyList<FolderVisit> Search(string text, int count)
    {
        lock (Gate)
            return Visits.Values.Where(v => v.Path.Contains(text, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(v => v.Count).ThenByDescending(v => v.LastVisited).Take(count).ToList();
    }

    public static void Clear()
    {
        lock (Gate) Visits.Clear();
        Changed?.Invoke();
    }

    // ───────── 状態ファイル ─────────

    public static void Load(IEnumerable<VisitState> saved)
    {
        lock (Gate)
        {
            Visits.Clear();
            foreach (var s in saved.Where(s => !string.IsNullOrWhiteSpace(s.Path) && s.Count > 0))
                Visits[Key(s.Path)] = new FolderVisit(Key(s.Path), s.Count, s.LastVisited);
        }
    }

    public static List<VisitState> ToState()
    {
        lock (Gate)
            return Visits.Values.OrderByDescending(v => v.LastVisited)
                .Select(v => new VisitState { Path = v.Path, Count = v.Count, LastVisited = v.LastVisited }).ToList();
    }
}
