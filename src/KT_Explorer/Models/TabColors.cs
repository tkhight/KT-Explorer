using System.Windows.Media;

namespace KT_Explorer.Models;

/// <summary>タブにつけられる色(名前と、#RRGGBB)。色なしは null / 空。</summary>
public static class TabColors
{
    public static readonly IReadOnlyList<(string Name, string Hex)> Palette = new (string, string)[]
    {
        ("赤", "#E5484D"),
        ("オレンジ", "#F5922B"),
        ("黄", "#E5B800"),
        ("緑", "#30A46C"),
        ("水色", "#1B9BD6"),
        ("青", "#3E63DD"),
        ("紫", "#8E4EC6"),
        ("ピンク", "#E93D82"),
        ("グレー", "#8B8D98"),
    };

    /// <summary>パレットにある色だけを受け付ける(状態ファイルが壊れていても落ちないように)。</summary>
    public static string? Normalize(string? hex) =>
        string.IsNullOrWhiteSpace(hex) ? null : Palette.Select(p => p.Hex).FirstOrDefault(h => string.Equals(h, hex.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>色のブラシ。alpha(0〜255)で透け具合を指定する。色なしは透明。</summary>
    public static Brush Brush(string? hex, byte alpha = 255)
    {
        if (Normalize(hex) is not { } h) return Brushes.Transparent;
        var c = (Color)ColorConverter.ConvertFromString(h);
        var b = new SolidColorBrush(Color.FromArgb(alpha, c.R, c.G, c.B));
        b.Freeze();
        return b;
    }
}
