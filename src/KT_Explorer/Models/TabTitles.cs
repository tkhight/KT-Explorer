namespace KT_Explorer.Models;

/// <summary>同じ名前のタブを見分けるための、添え字(親フォルダの名前)。</summary>
public static class TabTitles
{
    /// <summary>
    /// 各パスに添える名前を返す(添えないものは "")。フォルダ名が同じで場所が違うものがあるときだけ付ける。
    /// 近い親から順に見て、その階層の名前が、同じ名前のほかのパスと重ならない最初のものを使う
    /// (ProjectA\src\images と ProjectB\src\images なら、ProjectA と ProjectB)。
    /// </summary>
    public static IReadOnlyList<string> ParentHints(IReadOnlyList<string> paths)
    {
        var parts = paths.Select(p => p.TrimEnd('\\', '/').Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries)).ToList();
        var hints = Enumerable.Repeat("", paths.Count).ToArray();

        var sameName = Enumerable.Range(0, paths.Count)
            .Where(i => parts[i].Length > 0)
            .GroupBy(i => parts[i][^1], StringComparer.OrdinalIgnoreCase);
        foreach (var group in sameName)
        {
            // 同じフォルダを 2 つ開いていても、場所は 1 つと数える
            var places = group.GroupBy(i => string.Join('\\', parts[i]), StringComparer.OrdinalIgnoreCase).ToList();
            if (places.Count < 2) continue;

            var ancestors = places.Select(g => parts[g.First()][..^1].Reverse().ToArray()).ToList(); // 近い親が先頭
            for (int a = 0; a < places.Count; a++)
            {
                string hint = "";
                for (int level = 0; level < ancestors[a].Length && hint == ""; level++)
                {
                    string name = ancestors[a][level];
                    bool shared = Enumerable.Range(0, places.Count).Any(b => b != a && level < ancestors[b].Length
                        && string.Equals(ancestors[b][level], name, StringComparison.OrdinalIgnoreCase));
                    if (!shared) hint = name;
                }
                // どの階層の名前も、どれかと重なるときは、親までの道筋をそのまま出す
                if (hint == "") hint = string.Join('\\', ancestors[a].Reverse());
                foreach (var i in places[a]) hints[i] = hint;
            }
        }
        return hints;
    }
}
