using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace KT_Explorer.Services;

/// <summary>SHGetFileInfo によるシェルアイコン取得。拡張子単位でキャッシュする。</summary>
public static class ShellIconService
{
    private const uint SHGFI_ICON = 0x100;
    private const uint SHGFI_SMALLICON = 0x1;
    private const uint SHGFI_USEFILEATTRIBUTES = 0x10;
    private const uint FILE_ATTRIBUTE_DIRECTORY = 0x10;
    private const uint FILE_ATTRIBUTE_NORMAL = 0x80;

    private static readonly ConcurrentDictionary<string, ImageSource?> Cache = new(StringComparer.OrdinalIgnoreCase);
    // ファイルごとに固有のアイコンを持つ拡張子
    private static readonly HashSet<string> PerFileExt = new(StringComparer.OrdinalIgnoreCase) { ".exe", ".lnk", ".ico", ".dll", ".msi", ".scr" };

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);

    // 固有のアイコンを持つフォルダ(ドライブ、デスクトップ、ダウンロード、ドキュメントなど)。通常のフォルダは、共通のフォルダのアイコンを使い回す
    private static readonly Lazy<HashSet<string>> SpecialFolders = new(() =>
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Add(string? p) { if (!string.IsNullOrEmpty(p)) set.Add(p.TrimEnd('\\')); }
        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Add(profile);
        Add(Path.Combine(profile, "Downloads"));
        Add(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
        Add(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
        Add(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures));
        Add(Environment.GetFolderPath(Environment.SpecialFolder.MyMusic));
        Add(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos));
        return set;
    });

    /// <summary>固有のアイコンを持つフォルダか(ドライブのルート、または、デスクトップなどの特別なフォルダ)。</summary>
    public static bool HasOwnIcon(string path)
    {
        if (string.IsNullOrEmpty(path)) return false;
        string trimmed = path.TrimEnd('\\');
        // ドライブのルート(C: / C:\)
        if (trimmed.Length == 2 && trimmed[1] == ':') return true;
        return SpecialFolders.Value.Contains(trimmed);
    }

    public static ImageSource? GetIcon(string path, bool isDirectory)
    {
        string ext = isDirectory ? "<dir>" : Path.GetExtension(path);
        bool perFile = isDirectory ? HasOwnIcon(path) : PerFileExt.Contains(ext);
        string key = perFile ? path : ext;
        return Cache.GetOrAdd(key, _ => Load(path, isDirectory, perFile));
    }

    private static ImageSource? Load(string path, bool isDirectory, bool perFile)
    {
        var info = new SHFILEINFO();
        uint flags = SHGFI_ICON | SHGFI_SMALLICON;
        uint attr = isDirectory ? FILE_ATTRIBUTE_DIRECTORY : FILE_ATTRIBUTE_NORMAL;
        if (!perFile) flags |= SHGFI_USEFILEATTRIBUTES;
        SHGetFileInfo(path, attr, ref info, (uint)Marshal.SizeOf<SHFILEINFO>(), flags);
        if (info.hIcon == IntPtr.Zero) return null;
        try
        {
            var src = Imaging.CreateBitmapSourceFromHIcon(info.hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            src.Freeze();
            return src;
        }
        finally
        {
            DestroyIcon(info.hIcon);
        }
    }
}
