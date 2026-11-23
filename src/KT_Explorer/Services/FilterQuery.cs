using System.Globalization;
using System.Text.RegularExpressions;
using KT_Explorer.Models;

namespace KT_Explorer.Services;

/// <summary>
/// 「絞込」欄の入力を解釈した条件。空白で区切った条件を、すべて満たす(AND)項目だけを残す。
/// <list type="bullet">
/// <item>文字列: 名前に含む(大文字小文字は区別しない)。<c>*</c> と <c>?</c> を含むときは、名前全体のワイルドカード(<c>*.png</c>)。空白を含むときは <c>"二語 以上"</c> と引用符で囲む。</item>
/// <item><c>-</c> を先頭に付けると、その条件を満たさないもの(<c>-draft</c>、<c>-type:画像</c>)。</item>
/// <item><c>type:画像</c>(種類: 画像・動画・音声・文書・圧縮・コード・フォルダー・ファイル)、<c>ext:png,jpg</c>(拡張子)。</item>
/// <item><c>size:&gt;10MB</c>、<c>size:&lt;=500KB</c>、<c>size:1MB..10MB</c>(単位: B・KB・MB・GB・TB)。サイズを計算していないフォルダーは、サイズの条件に合わない。</item>
/// <item><c>date:今日</c> / 昨日 / 今週 / 今月 / 今年 / <c>7d</c>(7日以内)/ <c>&gt;2024-01-01</c> / <c>2024-01-01..2024-03-31</c>(更新日時)。</item>
/// </list>
/// </summary>
public sealed class FilterQuery
{
    private readonly List<Func<FileEntry, bool>> _conditions = new();
    private readonly List<string> _problems = new();

    public static FilterQuery Empty { get; } = new();

    /// <summary>解釈できなかった条件(その条件は無視される)。</summary>
    public IReadOnlyList<string> Problems => _problems;

    public bool IsEmpty => _conditions.Count == 0;

    public bool Matches(FileEntry entry)
    {
        foreach (var condition in _conditions)
            if (!condition(entry)) return false;
        return true;
    }

    // ───────── 解釈 ─────────

    private static readonly Regex TokenPattern = new("(?<neg>-?)(?:\"(?<q>[^\"]*)\"|(?<w>\\S+))", RegexOptions.Compiled);

    private static readonly Dictionary<string, string[]> TypeGroups = BuildTypeGroups();

    private static Dictionary<string, string[]> BuildTypeGroups()
    {
        var map = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        void Add(string[] names, string extensions)
        {
            var list = extensions.Split(',');
            foreach (var n in names) map[n] = list;
        }
        Add(new[] { "画像", "image", "images", "img" }, "png,jpg,jpeg,jpe,jfif,gif,bmp,tif,tiff,ico,webp,svg,heic,psd");
        Add(new[] { "動画", "video", "videos" }, "mp4,mov,avi,mkv,wmv,webm,flv,m4v,mpg,mpeg");
        Add(new[] { "音声", "音楽", "audio" }, "mp3,wav,flac,aac,ogg,m4a,wma");
        Add(new[] { "文書", "ドキュメント", "document", "documents", "doc", "docs" }, "txt,md,pdf,doc,docx,xls,xlsx,ppt,pptx,csv,rtf,odt");
        Add(new[] { "圧縮", "archive", "zip" }, "zip,7z,rar,tar,gz,tgz,bz2,xz,lzh");
        Add(new[] { "コード", "code" }, "cs,py,js,ts,tsx,jsx,cpp,c,h,hpp,java,go,rs,ps1,bat,cmd,sh,xaml,html,css,json,xml,yaml,yml,sql,mel");
        return map;
    }

    private static readonly HashSet<string> FolderTypes = new(StringComparer.OrdinalIgnoreCase) { "フォルダー", "フォルダ", "folder", "folders", "dir" };
    private static readonly HashSet<string> FileTypes = new(StringComparer.OrdinalIgnoreCase) { "ファイル", "file", "files" };

    public static FilterQuery Parse(string? text, DateTime now)
    {
        var query = new FilterQuery();
        if (string.IsNullOrWhiteSpace(text)) return query;

        foreach (Match m in TokenPattern.Matches(text))
        {
            bool negate = m.Groups["neg"].Value == "-";
            string token = m.Groups["q"].Success ? m.Groups["q"].Value : m.Groups["w"].Value;
            if (token.Length == 0 || token == "-") continue; // "-" だけ、"" だけ

            Func<FileEntry, bool>? condition = null;
            int colon = token.IndexOf(':');
            if (!m.Groups["q"].Success && colon > 0)
            {
                string key = token[..colon].ToLowerInvariant();
                string value = token[(colon + 1)..];
                switch (key)
                {
                    case "type" or "種類":
                        condition = ParseType(value);
                        break;
                    case "ext" or "拡張子":
                        condition = ParseExtensions(value);
                        break;
                    case "size" or "サイズ":
                        condition = ParseSize(value);
                        break;
                    case "date" or "日付" or "更新":
                        condition = ParseDate(value, now);
                        break;
                    default:
                        condition = Name(token); // 「a:b」のような、条件ではない名前
                        break;
                }
                if (condition == null && key is "type" or "種類" or "ext" or "拡張子" or "size" or "サイズ" or "date" or "日付" or "更新")
                {
                    query._problems.Add($"条件を解釈できません: {token}");
                    continue; // その条件は無視する(全部を隠してしまわない)
                }
            }
            else
            {
                condition = Name(token);
            }

            var c = condition!;
            query._conditions.Add(negate ? e => !c(e) : c);
        }
        return query;
    }

    // ───────── 名前 ─────────

    private static Func<FileEntry, bool> Name(string token)
    {
        if (token.IndexOfAny(new[] { '*', '?' }) >= 0)
        {
            var regex = new Regex("^" + Regex.Escape(token).Replace("\\*", ".*").Replace("\\?", ".") + "$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            return e => regex.IsMatch(e.Name);
        }
        return e => e.Name.Contains(token, StringComparison.CurrentCultureIgnoreCase);
    }

    // ───────── 種類・拡張子 ─────────

    private static Func<FileEntry, bool>? ParseType(string value)
    {
        if (FolderTypes.Contains(value)) return e => e.IsDirectory;
        if (FileTypes.Contains(value)) return e => !e.IsDirectory;
        if (TypeGroups.TryGetValue(value, out var extensions)) return HasExtension(extensions);
        return null;
    }

    private static Func<FileEntry, bool>? ParseExtensions(string value)
    {
        var list = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => x.TrimStart('.')).Where(x => x.Length > 0).ToArray();
        return list.Length == 0 ? null : HasExtension(list);
    }

    private static Func<FileEntry, bool> HasExtension(string[] extensions)
    {
        var set = new HashSet<string>(extensions, StringComparer.OrdinalIgnoreCase);
        return e => !e.IsDirectory && set.Contains(System.IO.Path.GetExtension(e.Name).TrimStart('.'));
    }

    // ───────── サイズ ─────────

    private static readonly Regex SizeValue = new(@"^(?<n>\d+(?:\.\d+)?)(?<u>tb|gb|mb|kb|t|g|m|k|b)?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static Func<FileEntry, bool>? ParseSize(string value)
    {
        int range = value.IndexOf("..", StringComparison.Ordinal);
        if (range > 0)
        {
            var low = SizeBytes(value[..range]);
            var high = SizeBytes(value[(range + 2)..]);
            if (low == null || high == null) return null;
            return e => e.Size is long s && s >= low && s <= high;
        }

        string op = "=";
        string rest = value;
        foreach (var candidate in new[] { ">=", "<=", ">", "<", "=" })
        {
            if (value.StartsWith(candidate, StringComparison.Ordinal)) { op = candidate; rest = value[candidate.Length..]; break; }
        }
        var bytes = SizeBytes(rest);
        if (bytes == null) return null;
        long limit = bytes.Value;
        return op switch
        {
            ">=" => e => e.Size is long s && s >= limit,
            "<=" => e => e.Size is long s && s <= limit,
            ">" => e => e.Size is long s && s > limit,
            "<" => e => e.Size is long s && s < limit,
            _ => e => e.Size is long s && s == limit,
        };
    }

    private static long? SizeBytes(string text)
    {
        var m = SizeValue.Match(text.Trim());
        if (!m.Success) return null;
        double n = double.Parse(m.Groups["n"].Value, CultureInfo.InvariantCulture);
        double unit = m.Groups["u"].Value.ToLowerInvariant() switch
        {
            "k" or "kb" => 1024d,
            "m" or "mb" => 1024d * 1024,
            "g" or "gb" => 1024d * 1024 * 1024,
            "t" or "tb" => 1024d * 1024 * 1024 * 1024,
            _ => 1d,
        };
        return (long)(n * unit);
    }

    // ───────── 更新日時 ─────────

    private static readonly Regex RecentDays = new(@"^(?<n>\d+)(?:d|日)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly string[] DateFormats = { "yyyy-MM-dd", "yyyy/MM/dd", "yyyyMMdd", "yyyy-M-d", "yyyy/M/d" };

    private static Func<FileEntry, bool>? ParseDate(string value, DateTime now)
    {
        string v = value.Trim().ToLowerInvariant();
        var today = now.Date;

        switch (v)
        {
            case "今日" or "today":
                return e => e.Modified.Date == today;
            case "昨日" or "yesterday":
                return e => e.Modified.Date == today.AddDays(-1);
            case "今週" or "week" or "thisweek":
            {
                int fromMonday = ((int)today.DayOfWeek + 6) % 7; // 月曜日を週の始まりにする
                var start = today.AddDays(-fromMonday);
                return e => e.Modified >= start && e.Modified <= now;
            }
            case "今月" or "month" or "thismonth":
            {
                var start = new DateTime(today.Year, today.Month, 1);
                return e => e.Modified >= start && e.Modified <= now;
            }
            case "今年" or "year" or "thisyear":
            {
                var start = new DateTime(today.Year, 1, 1);
                return e => e.Modified >= start && e.Modified <= now;
            }
        }

        var days = RecentDays.Match(v);
        if (days.Success)
        {
            var since = now.AddDays(-int.Parse(days.Groups["n"].Value, CultureInfo.InvariantCulture));
            return e => e.Modified >= since && e.Modified <= now;
        }

        int range = v.IndexOf("..", StringComparison.Ordinal);
        if (range > 0)
        {
            var a = ParseDay(v[..range]);
            var b = ParseDay(v[(range + 2)..]);
            if (a == null || b == null) return null;
            var from = a.Value.Date;
            var to = b.Value.Date;
            return e => e.Modified.Date >= from && e.Modified.Date <= to;
        }

        string op = "=";
        string rest = v;
        foreach (var candidate in new[] { ">=", "<=", ">", "<", "=" })
        {
            if (v.StartsWith(candidate, StringComparison.Ordinal)) { op = candidate; rest = v[candidate.Length..]; break; }
        }
        var day = ParseDay(rest);
        if (day == null) return null;
        var date = day.Value.Date;
        return op switch
        {
            ">=" => e => e.Modified.Date >= date,
            "<=" => e => e.Modified.Date <= date,
            ">" => e => e.Modified.Date > date,
            "<" => e => e.Modified.Date < date,
            _ => e => e.Modified.Date == date,
        };
    }

    private static DateTime? ParseDay(string text) =>
        DateTime.TryParseExact(text.Trim(), DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
}
