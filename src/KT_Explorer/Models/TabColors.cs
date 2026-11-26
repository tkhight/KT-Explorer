using System.Text.RegularExpressions;
using System.Windows.Media;

namespace KT_Explorer.Models;

/// <summary>
/// タブとグループにつけられる色。色の指定は #RRGGBB(パレットの色か、「その他の色」で選んだ任意の色)。色なしは null / 空。
/// </summary>
public static class TabColors
{
    /// <summary>格子で選ぶ色(24 色。6 列 × 4 段の並び順)。</summary>
    public static readonly IReadOnlyList<(string Name, string Hex)> Palette = new (string, string)[]
    {
        ("赤", "#E5484D"), ("朱", "#F76B15"), ("オレンジ", "#F5922B"), ("琥珀", "#FFB224"), ("黄", "#E5B800"), ("黄緑", "#8DB600"),
        ("若草", "#5BB98B"), ("緑", "#30A46C"), ("深緑", "#2E7D32"), ("青緑", "#12A594"), ("空色", "#4CC9F0"), ("水色", "#1B9BD6"),
        ("青", "#3E63DD"), ("紺", "#2A3B8F"), ("藤", "#7C7CE0"), ("紫", "#8E4EC6"), ("赤紫", "#AB4ABA"), ("桃", "#F76C9E"),
        ("ピンク", "#E93D82"), ("えんじ", "#9B2C4B"), ("茶", "#A0713C"), ("こげ茶", "#6F4E37"), ("グレー", "#8B8D98"), ("青灰", "#5F7080"),
    };

    private static readonly Regex HexPattern = new("^#[0-9A-Fa-f]{6}$");

    /// <summary>#RRGGBB(大文字)にそろえる。形が違うものは、色なし(状態ファイルが壊れていても落ちないように)。</summary>
    public static string? Normalize(string? hex)
    {
        var h = hex?.Trim();
        return h != null && HexPattern.IsMatch(h) ? h.ToUpperInvariant() : null;
    }

    /// <summary>色のブラシ。alpha(0〜255)で透け具合を指定する。色なしは透明。</summary>
    public static Brush Brush(string? hex, byte alpha = 255)
    {
        if (Normalize(hex) is not { } h) return Brushes.Transparent;
        var c = (Color)ColorConverter.ConvertFromString(h);
        return Frozen(Color.FromArgb(alpha, c.R, c.G, c.B));
    }

    /// <summary>白に amount(0〜1)の割合で色を混ぜた、不透明のブラシ。一覧の背景に使う(どんな色でも、文字が読める薄さになる)。色なしは白。</summary>
    public static Brush Pale(string? hex, double amount)
    {
        if (Normalize(hex) is not { } h) return Brushes.White;
        var c = (Color)ColorConverter.ConvertFromString(h);
        byte Mix(byte v) => (byte)Math.Round(255 - (255 - v) * amount);
        return Frozen(Color.FromRgb(Mix(c.R), Mix(c.G), Mix(c.B)));
    }

    private static Brush Frozen(Color color)
    {
        var b = new SolidColorBrush(color);
        b.Freeze();
        return b;
    }
}
