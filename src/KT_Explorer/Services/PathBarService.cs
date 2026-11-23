using System.IO;

namespace KT_Explorer.Services;

/// <summary>パンくず(パスの各階層)の1つ分。</summary>
public sealed record PathSegment(string Name, string FullPath);

/// <summary>パス欄の補助: 入力途中のパスの補完候補と、パンくず(階層ごとに分けたパス)。</summary>
public static class PathBarService
{
    /// <summary>
    /// 入力中の text に合う、フォルダの候補(最大 max 件)。
    /// 「C:\Us」なら C:\ の中の Us で始まるフォルダ、「C:\Users\」ならその中のフォルダ。
    /// 区切りの無い文字(「proj」など)は、開いたことのあるフォルダの履歴から、パスに含むものを探す。
    /// </summary>
    public static IReadOnlyList<string> Suggest(string text, int max = 12)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(text) || max <= 0) return result;
        text = text.Trim().Trim('"');

        int sep = text.LastIndexOfAny(new[] { '\\', '/' });
        if (sep >= 0)
        {
            string dir = text[..(sep + 1)];
            string prefix = text[(sep + 1)..];
            if (dir.Length == 2 && dir[1] == ':') dir += "\\";
            try
            {
                if (Directory.Exists(dir))
                {
                    foreach (var sub in Directory.EnumerateDirectories(dir).OrderBy(p => p, StringComparer.CurrentCultureIgnoreCase))
                    {
                        string name = Path.GetFileName(sub);
                        if (!name.StartsWith(prefix, StringComparison.CurrentCultureIgnoreCase)) continue;
                        if ((File.GetAttributes(sub) & (FileAttributes.Hidden | FileAttributes.System)) != 0 && prefix.Length == 0) continue; // 隠し・システムのフォルダは、名前を打ち始めるまで出さない
                        result.Add(sub);
                        if (result.Count >= max) return result;
                    }
                }
            }
            catch (Exception) { /* 読めない場所は、候補なし */ }
        }

        // 履歴(既に出したものは重ねない)
        foreach (var visit in FolderHistory.Search(text, max))
        {
            if (result.Count >= max) break;
            if (result.Any(r => string.Equals(r.TrimEnd('\\'), visit.Path.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))) continue;
            if (string.Equals(visit.Path.TrimEnd('\\'), text.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase)) continue; // 入力そのもの
            result.Add(visit.Path);
        }
        return result;
    }

    /// <summary>パスを階層ごとに分ける(C:\a\b → C:\ / a / b。それぞれ、そこまでのフルパスを持つ)。</summary>
    public static IReadOnlyList<PathSegment> Split(string path)
    {
        var list = new List<PathSegment>();
        if (string.IsNullOrWhiteSpace(path)) return list;
        string trimmed = path.Trim().TrimEnd('\\', '/');

        string root;
        string rest;
        if (trimmed.StartsWith(@"\\", StringComparison.Ordinal)) // \\server\share\...
        {
            var parts = trimmed[2..].Split('\\', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return list;
            root = parts.Length >= 2 ? $@"\\{parts[0]}\{parts[1]}" : $@"\\{parts[0]}";
            rest = string.Join('\\', parts.Skip(2));
            list.Add(new PathSegment(root, root));
        }
        else
        {
            int first = trimmed.IndexOf('\\');
            root = first < 0 ? trimmed : trimmed[..first];
            rest = first < 0 ? "" : trimmed[(first + 1)..];
            string rootPath = root.EndsWith(':') ? root + "\\" : root;
            list.Add(new PathSegment(rootPath, rootPath));
            root = rootPath;
        }

        string current = root.TrimEnd('\\');
        foreach (var name in rest.Split('\\', StringSplitOptions.RemoveEmptyEntries))
        {
            current += "\\" + name;
            list.Add(new PathSegment(name, current));
        }
        return list;
    }

    /// <summary>あるフォルダの中のフォルダ(パンくずの「▸」を押したときの一覧)。読めなければ空。</summary>
    public static IReadOnlyList<PathSegment> Children(string path)
    {
        try
        {
            return Directory.EnumerateDirectories(path)
                .Where(p => (File.GetAttributes(p) & (FileAttributes.Hidden | FileAttributes.System)) == 0)
                .OrderBy(p => p, StringComparer.CurrentCultureIgnoreCase)
                .Select(p => new PathSegment(Path.GetFileName(p), p)).ToList();
        }
        catch (Exception) { return Array.Empty<PathSegment>(); }
    }
}
