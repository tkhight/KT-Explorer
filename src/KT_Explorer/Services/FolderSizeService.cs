using System.IO;

namespace KT_Explorer.Services;

/// <summary>フォルダのサイズ計算の設定。アプリ全体で1つ(設定メニューで切り替える)。</summary>
public static class FolderSizeSettings
{
    /// <summary>一覧を開いたとき、フォルダの合計サイズを自動で計算する(重い処理のため、既定はオフ)。</summary>
    public static bool Auto { get; set; }
}

/// <summary>フォルダの合計サイズ(中のすべてのファイルの大きさの合計)を計算する。</summary>
public static class FolderSizeService
{
    /// <summary>
    /// path 以下のファイルの合計サイズ(バイト)を返す。アクセスできないフォルダは飛ばす。
    /// ジャンクションやシンボリックリンクのフォルダは、たどらない(ループや二重計算を避けるため)。
    /// 取り消されたら OperationCanceledException。
    /// </summary>
    public static long Calculate(string path, CancellationToken ct)
    {
        long total = 0;
        var pending = new Stack<string>();
        pending.Push(path);
        var options = new EnumerationOptions { IgnoreInaccessible = true, RecurseSubdirectories = false, AttributesToSkip = 0 };

        while (pending.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var dir = pending.Pop();
            IEnumerable<FileSystemInfo> entries;
            try { entries = new DirectoryInfo(dir).EnumerateFileSystemInfos("*", options); }
            catch { continue; }

            try
            {
                foreach (var info in entries)
                {
                    if (info is FileInfo file)
                    {
                        try { total += file.Length; } catch { /* 読めないファイルは数えない */ }
                    }
                    else if ((info.Attributes & FileAttributes.ReparsePoint) == 0)
                    {
                        pending.Push(info.FullName);
                    }
                }
            }
            catch (IOException) { /* 列挙の途中で消えた・アクセスできなくなった */ }
            catch (UnauthorizedAccessException) { }
        }
        return total;
    }
}
