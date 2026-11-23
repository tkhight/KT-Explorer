using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;

namespace KT_Explorer.Services;

/// <summary>画像・動画・音声の情報(画像のサイズ、再生時間)。</summary>
public sealed record MediaInfo(int Width, int Height, TimeSpan? Duration)
{
    public static readonly MediaInfo None = new(0, 0, null);

    /// <summary>「1920 × 1080」。サイズが分からなければ空。</summary>
    public string DimensionsText => Width > 0 && Height > 0 ? $"{Width} × {Height}" : "";

    /// <summary>「3:25」「1:02:03」。再生時間が分からなければ空。</summary>
    public string DurationText => FormatDuration(Duration);

    public static string FormatDuration(TimeSpan? duration)
    {
        if (duration is not { } d || d <= TimeSpan.Zero) return "";
        var whole = TimeSpan.FromSeconds(Math.Round(d.TotalSeconds));
        return whole.TotalHours >= 1 ? $"{(int)whole.TotalHours}:{whole.Minutes:00}:{whole.Seconds:00}" : $"{whole.Minutes}:{whole.Seconds:00}";
    }
}

/// <summary>
/// ファイルのプロパティ(Windows が持つ情報)から、画像のサイズ・動画/音声の再生時間を読む。
/// 読み込みは専用のスレッドで順に行い、画面を止めない。一度読んだものは、更新日時の目印ごとに記憶する。
/// </summary>
public static class MediaInfoService
{
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        "png", "jpg", "jpeg", "jpe", "jfif", "gif", "bmp", "tif", "tiff", "ico", "webp", "heic",
        "mp4", "mov", "avi", "mkv", "wmv", "webm", "m4v", "mpg", "mpeg", "flv",
        "mp3", "wav", "flac", "aac", "ogg", "m4a", "wma",
    };

    /// <summary>情報を読む対象の拡張子か(それ以外のファイルは、読みに行かない)。</summary>
    public static bool IsCandidate(string path) => Extensions.Contains(Path.GetExtension(path).TrimStart('.'));

    private static readonly ConcurrentDictionary<string, MediaInfo> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly BlockingCollection<(string Path, string Key, TaskCompletionSource<MediaInfo> Done)> Queue = new();
    private static int _started;

    /// <summary>記憶してあれば、すぐに返す。</summary>
    public static bool TryGetCached(string path, long stamp, out MediaInfo info) => Cache.TryGetValue(CacheKey(path, stamp), out info!);

    public static void ClearCache() => Cache.Clear();

    private static string CacheKey(string path, long stamp) => stamp + "|" + path;

    public static Task<MediaInfo> GetAsync(string path, long stamp)
    {
        string key = CacheKey(path, stamp);
        if (Cache.TryGetValue(key, out var cached)) return Task.FromResult(cached);
        EnsureWorker();
        var done = new TaskCompletionSource<MediaInfo>(TaskCreationOptions.RunContinuationsAsynchronously);
        Queue.Add((path, key, done));
        return done.Task;
    }

    private static void EnsureWorker()
    {
        if (Interlocked.Exchange(ref _started, 1) != 0) return;
        var thread = new Thread(Work) { IsBackground = true, Name = "KT_MediaInfo" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }

    private static void Work()
    {
        foreach (var (path, key, done) in Queue.GetConsumingEnumerable())
        {
            MediaInfo info;
            try { info = Read(path); } catch (Exception) { info = MediaInfo.None; }
            if (Cache.Count > 20000) Cache.Clear(); // 上限(メモリを使いすぎない)
            Cache[key] = info;
            done.TrySetResult(info);
        }
    }

    // ───────── プロパティの読み取り ─────────

    private static readonly Guid ImageGuid = new("6444048F-4C8B-11D1-8B70-080036B11A03");   // System.Image.*
    private static readonly Guid VideoGuid = new("64440491-4C8B-11D1-8B70-080036B11A03");   // System.Video.*
    private static readonly Guid MediaGuid = new("64440490-4C8B-11D1-8B70-080036B11A03");   // System.Media.*

    private static MediaInfo Read(string path)
    {
        var iid = typeof(IPropertyStore).GUID;
        if (SHGetPropertyStoreFromParsingName(path, IntPtr.Zero, 0 /* GPS_DEFAULT */, ref iid, out var store) != 0 || store == null)
            return MediaInfo.None;
        try
        {
            long w = GetUInt(store, ImageGuid, 3), h = GetUInt(store, ImageGuid, 4);          // 画像の幅・高さ
            if (w <= 0 || h <= 0) { w = GetUInt(store, VideoGuid, 3); h = GetUInt(store, VideoGuid, 4); } // 動画のフレームの幅・高さ
            long ticks = GetUInt(store, MediaGuid, 3);                                          // 再生時間(100ns 単位)
            return new MediaInfo((int)Math.Max(0, w), (int)Math.Max(0, h), ticks > 0 ? TimeSpan.FromTicks(ticks) : null);
        }
        finally { Marshal.ReleaseComObject(store); }
    }

    private static long GetUInt(IPropertyStore store, Guid fmtid, uint pid)
    {
        var key = new PropertyKey { fmtid = fmtid, pid = pid };
        PropVariant pv = default;
        try
        {
            if (store.GetValue(ref key, out pv) != 0) return 0;
            if (pv.vt == 0) return 0; // VT_EMPTY
            return (long)PropVariantToUInt64WithDefault(ref pv, 0);
        }
        finally { PropVariantClear(ref pv); }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct PropertyKey { public Guid fmtid; public uint pid; }

    // PROPVARIANT: 先頭の vt(型)だけを見る。値の取り出しは、シェルの関数に任せる(64 ビットで 24 バイト)。
    [StructLayout(LayoutKind.Sequential)]
    private struct PropVariant { public ushort vt; public ushort r1, r2, r3; public IntPtr a; public IntPtr b; }

    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetAt(uint index, out PropertyKey key);
        [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);
        [PreserveSig] int SetValue(ref PropertyKey key, ref PropVariant value);
        [PreserveSig] int Commit();
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHGetPropertyStoreFromParsingName(string path, IntPtr bindContext, int flags, ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IPropertyStore store);

    [DllImport("propsys.dll")]
    private static extern ulong PropVariantToUInt64WithDefault(ref PropVariant pv, ulong defaultValue);

    [DllImport("ole32.dll")]
    private static extern int PropVariantClear(ref PropVariant pv);
}
