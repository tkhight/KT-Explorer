using System.IO;
using System.Windows.Media;
using KT_Explorer.Services;

namespace KT_Explorer.Models;

/// <summary>ファイル一覧の1行分。</summary>
public sealed class FileEntry
{
    public FileEntry(FileSystemInfo info)
    {
        Name = info.Name;
        FullPath = info.FullName;
        IsDirectory = info is DirectoryInfo;
        Modified = info.LastWriteTime;
        if (info is FileInfo fi)
        {
            try { Size = fi.Length; } catch { Size = null; }
        }
    }

    public string Name { get; }
    public string FullPath { get; }
    public bool IsDirectory { get; }
    public DateTime Modified { get; }
    public long? Size { get; }

    public string TypeText => IsDirectory
        ? "ファイル フォルダー"
        : (string.IsNullOrEmpty(Path.GetExtension(Name)) ? "ファイル" : Path.GetExtension(Name).TrimStart('.').ToUpperInvariant() + " ファイル");

    public string ModifiedText => Modified.ToString("yyyy/MM/dd HH:mm");

    public string SizeText => Size is null ? "" : FormatSize(Size.Value);

    /// <summary>シェルアイコン(拡張子単位でキャッシュ)。</summary>
    public ImageSource? Icon => ShellIconService.GetIcon(FullPath, IsDirectory);

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        double kb = bytes / 1024.0;
        if (kb < 1024) return $"{kb:N0} KB";
        double mb = kb / 1024.0;
        if (mb < 1024) return $"{mb:N1} MB";
        return $"{mb / 1024.0:N2} GB";
    }
}
