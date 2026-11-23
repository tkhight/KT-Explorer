namespace KT_Explorer.Services;

/// <summary>アイコン表示の項目の大きさ。</summary>
public enum IconSize
{
    Small,
    Medium,
    Large,
}

/// <summary>一覧の見た目の、アプリ全体の設定(アイコンの大きさ。状態ファイルに保存する)。</summary>
public static class ViewSettings
{
    private static IconSize _iconSize = IconSize.Medium;

    /// <summary>アイコン表示の大きさ。変えると、<see cref="Changed"/> が起きる。</summary>
    public static IconSize IconSize
    {
        get => _iconSize;
        set
        {
            if (_iconSize == value) return;
            _iconSize = value;
            ThumbnailService.RequestSize = ThumbnailPixels;
            Changed?.Invoke();
        }
    }

    public static event Action? Changed;

    // ───────── 詳細表示の、追加の列(作成日時・属性・画像のサイズ・再生時間) ─────────

    /// <summary>追加できる列(キー、見出し)。表示の順は、この並びのとおり。</summary>
    public static readonly IReadOnlyList<(string Key, string Header)> ExtraColumnCatalog = new[]
    {
        ("created", "作成日時"),
        ("attributes", "属性"),
        ("dimensions", "画像のサイズ"),
        ("duration", "再生時間"),
    };

    private static readonly HashSet<string> _extraColumns = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>追加の列の表示の切り替わり(全タブ共通)。</summary>
    public static event Action? ColumnsChanged;

    /// <summary>表示している追加の列(カタログの並び順)。</summary>
    public static IReadOnlyList<string> ExtraColumns => ExtraColumnCatalog.Where(c => _extraColumns.Contains(c.Key)).Select(c => c.Key).ToList();

    public static bool IsColumnVisible(string key) => _extraColumns.Contains(key);

    public static void SetColumnVisible(string key, bool visible)
    {
        if (!ExtraColumnCatalog.Any(c => c.Key == key)) return;
        bool changed = visible ? _extraColumns.Add(key) : _extraColumns.Remove(key);
        if (changed) ColumnsChanged?.Invoke();
    }

    /// <summary>保存された列の表示を読み込む(変更の通知は起こさない)。</summary>
    public static void LoadColumns(IEnumerable<string>? keys)
    {
        _extraColumns.Clear();
        foreach (var k in keys ?? Enumerable.Empty<string>())
            if (ExtraColumnCatalog.Any(c => c.Key == k)) _extraColumns.Add(k);
    }

    /// <summary>アイコンの画像の表示の大きさ(1辺、論理ピクセル)。</summary>
    public static double IconImageSize => _iconSize switch { IconSize.Small => 48, IconSize.Large => 128, _ => 80 };

    /// <summary>アイコン1つの幅(名前の折り返しの幅)。</summary>
    public static double IconItemWidth => _iconSize switch { IconSize.Small => 80, IconSize.Large => 156, _ => 108 };

    /// <summary>シェルに頼むサムネイルの大きさ(表示より少し大きくして、拡大してもぼやけないようにする)。</summary>
    public static int ThumbnailPixels => _iconSize switch { IconSize.Small => 64, IconSize.Large => 160, _ => 96 };

    /// <summary>保存された大きさを読み込む(変更の通知は起こさない)。</summary>
    public static void Load(IconSize size)
    {
        _iconSize = size;
        ThumbnailService.RequestSize = ThumbnailPixels;
    }
}
