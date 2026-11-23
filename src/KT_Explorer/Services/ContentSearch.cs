using System.IO;

namespace KT_Explorer.Services;

/// <summary>ファイルの中身(テキスト)の検索。文字コードは、プレビューと同じ判定(BOM → UTF-8 → Shift-JIS)。</summary>
public static class ContentSearch
{
    /// <summary>調べる先頭のバイト数(これより大きいファイルは、先頭だけを調べる)。</summary>
    public const int MaxBytes = 2 * 1024 * 1024;

    /// <summary>テキストファイルの先頭 MaxBytes に term を含むか。バイナリ・読めないファイルは false。</summary>
    public static bool FileContains(string path, string term, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(term)) return false;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var buffer = new byte[(int)Math.Min(stream.Length, MaxBytes)];
            int read = 0;
            while (read < buffer.Length)
            {
                ct.ThrowIfCancellationRequested();
                int n = stream.Read(buffer, read, buffer.Length - read);
                if (n == 0) break;
                read += n;
            }
            string? text = PreviewLoader.Decode(buffer, read, truncated: stream.Length > read);
            return text != null && text.Contains(term, StringComparison.CurrentCultureIgnoreCase);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { return false; }
    }
}
