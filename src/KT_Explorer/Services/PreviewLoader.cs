using System.IO;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using KT_Explorer.Models;

namespace KT_Explorer.Services;

public enum PreviewKind { None, Image, Text, Info }

/// <summary>プレビューに表示する内容。</summary>
/// <param name="Info">種類・大きさ・日時などの説明(画像・テキストのときは、内容の下に出す)。</param>
public sealed record PreviewContent(PreviewKind Kind, string Info, ImageSource? Image = null, string? Text = null);

/// <summary>
/// ファイル・フォルダのプレビューの内容を作る。重い処理(ファイルの読み込み・画像のデコード)を含むため、
/// 画面のスレッドでなく、バックグラウンドで呼ぶ。
/// </summary>
public static class PreviewLoader
{
    /// <summary>テキストは、先頭のこの大きさだけを読む。</summary>
    public const int MaxTextBytes = 64 * 1024;

    /// <summary>画像は、これより広いときは、この幅に縮小して読み込む(メモリを抑える)。</summary>
    public const int MaxImageWidth = 1600;

    private const int FolderCountLimit = 5000;

    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".png", ".jpg", ".jpeg", ".jpe", ".gif", ".bmp", ".tif", ".tiff", ".ico", ".webp", ".jfif" };

    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".md", ".markdown", ".log", ".json", ".xml", ".yaml", ".yml", ".toml", ".ini", ".cfg", ".conf", ".csv", ".tsv",
        ".cs", ".csproj", ".sln", ".xaml", ".py", ".pyw", ".ps1", ".psm1", ".bat", ".cmd", ".sh", ".js", ".ts", ".tsx", ".jsx",
        ".html", ".htm", ".css", ".scss", ".sql", ".c", ".h", ".cpp", ".hpp", ".java", ".kt", ".go", ".rs", ".rb", ".php",
        ".mel", ".usda", ".gitignore", ".editorconfig", ".gitattributes", ".env", ".properties", ".bas", ".vbs", ".reg",
    };

    static PreviewLoader()
    {
        // 日本語の古いテキスト(Shift-JIS)を読むために、コードページを使えるようにする
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    /// <summary>プレビューの内容を作る。取り消されたら OperationCanceledException。</summary>
    public static PreviewContent Load(string path, bool isDirectory, CancellationToken ct)
    {
        try
        {
            return isDirectory ? LoadFolder(path, ct) : LoadFile(path, ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new PreviewContent(PreviewKind.Info, "プレビューできません: " + ex.Message);
        }
    }

    // ───────── フォルダ ─────────

    private static PreviewContent LoadFolder(string path, CancellationToken ct)
    {
        var dir = new DirectoryInfo(path);
        if (!dir.Exists) return new PreviewContent(PreviewKind.Info, "フォルダーが見つかりません。");

        int folders = 0, files = 0;
        bool capped = false;
        foreach (var e in dir.EnumerateFileSystemInfos())
        {
            ct.ThrowIfCancellationRequested();
            if (e is DirectoryInfo) folders++; else files++;
            if (folders + files >= FolderCountLimit) { capped = true; break; }
        }
        string count = capped ? $"{FolderCountLimit} 件以上" : $"{folders} 個のフォルダー、{files} 個のファイル";
        return new PreviewContent(PreviewKind.Info,
            $"フォルダー\n\n中身: {count}\n更新日時: {dir.LastWriteTime:yyyy/MM/dd HH:mm}\n場所: {path}");
    }

    // ───────── ファイル ─────────

    private static PreviewContent LoadFile(string path, CancellationToken ct)
    {
        var file = new FileInfo(path);
        if (!file.Exists) return new PreviewContent(PreviewKind.Info, "ファイルが見つかりません。");

        string ext = file.Extension;
        string info = Describe(file);

        if (ImageExtensions.Contains(ext))
        {
            var (image, width, height) = LoadImage(path);
            ct.ThrowIfCancellationRequested();
            return new PreviewContent(PreviewKind.Image, info + $"\n画像: {width} × {height} px", image);
        }

        // 既知のテキスト、または、拡張子では分からないファイル(中身を見て判断する)
        var text = ReadText(path, file.Length, out bool truncated, ct);
        if (text != null)
        {
            if (truncated) text += $"\n\n… 先頭の {MaxTextBytes / 1024} KB だけを表示しています。";
            return new PreviewContent(PreviewKind.Text, info, Text: text);
        }

        return new PreviewContent(PreviewKind.Info, info + (TextExtensions.Contains(ext) ? "\n\nテキストとして読めませんでした。" : "\n\nこの種類のファイルは、プレビューに対応していません。"));
    }

    private static string Describe(FileInfo file)
    {
        string type = string.IsNullOrEmpty(file.Extension) ? "ファイル" : file.Extension.TrimStart('.').ToUpperInvariant() + " ファイル";
        return $"{type}\n大きさ: {FileEntry.FormatSize(file.Length)}({file.Length:N0} バイト)\n更新日時: {file.LastWriteTime:yyyy/MM/dd HH:mm}\n作成日時: {file.CreationTime:yyyy/MM/dd HH:mm}";
    }

    // ───────── 画像 ─────────

    /// <summary>画像を読み込む。大きな画像は縮小して読み込み、画面のスレッド以外でも使えるよう凍結する。</summary>
    private static (ImageSource Image, int Width, int Height) LoadImage(string path)
    {
        int width, height;
        using (var probe = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            var frame = BitmapFrame.Create(probe, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
            width = frame.PixelWidth;
            height = frame.PixelHeight;
        }

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        if (width > MaxImageWidth) bmp.DecodePixelWidth = MaxImageWidth;
        bmp.StreamSource = stream;
        bmp.EndInit();
        bmp.Freeze();
        return (bmp, width, height);
    }

    // ───────── テキスト ─────────

    /// <summary>先頭の MaxTextBytes を読み、テキストとして読めれば文字列を、バイナリなら null を返す。</summary>
    private static string? ReadText(string path, long length, out bool truncated, CancellationToken ct)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var buffer = new byte[MaxTextBytes];
        int read = 0;
        while (read < buffer.Length)
        {
            ct.ThrowIfCancellationRequested();
            int n = stream.Read(buffer, read, buffer.Length - read);
            if (n == 0) break;
            read += n;
        }
        truncated = length > read;
        return Decode(buffer, read, truncated);
    }

    /// <summary>文字コードを判定して文字列にする(BOM → UTF-8 → Shift-JIS の順)。バイナリと思われるときは null。</summary>
    internal static string? Decode(byte[] bytes, int count, bool truncated)
    {
        if (count == 0) return "";

        // BOM
        if (count >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return DecodeTrimmed(new UTF8Encoding(false, true), bytes, 3, count - 3, truncated);
        if (count >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE) return Encoding.Unicode.GetString(bytes, 2, count - 2 - (count % 2));
        if (count >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF) return Encoding.BigEndianUnicode.GetString(bytes, 2, count - 2 - (count % 2));

        // BOM が無いのに、0 のバイトがあるものは、バイナリ
        if (Array.IndexOf(bytes, (byte)0, 0, count) >= 0) return null;

        var utf8 = DecodeTrimmed(new UTF8Encoding(false, true), bytes, 0, count, truncated);
        if (utf8 != null) return utf8;

        // UTF-8 として読めなければ、Shift-JIS(日本語の古いファイル)として試す。置換文字だらけならバイナリ
        var sjis = Encoding.GetEncoding(932).GetString(bytes, 0, count);
        int bad = sjis.Count(c => c == '�');
        return bad * 20 > sjis.Length ? null : sjis;
    }

    /// <summary>厳密に読む。途中で切った末尾の文字(最大3バイト)が壊れていても、読める。</summary>
    private static string? DecodeTrimmed(Encoding strict, byte[] bytes, int index, int count, bool truncated)
    {
        for (int trim = 0; trim <= (truncated ? 3 : 0) && trim <= count; trim++)
        {
            try { return strict.GetString(bytes, index, count - trim); }
            catch (DecoderFallbackException) { /* もう1バイト減らして試す */ }
        }
        return null;
    }
}
