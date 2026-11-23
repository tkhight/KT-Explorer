using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace KT_Explorer.Services;

/// <summary>
/// アイコン表示で使う、大きなアイコン・サムネイルの取得(Windows のシェルが持つ、エクスプローラーと同じもの)。
/// 画像・動画・文書などは中身のサムネイル、それ以外は大きなアイコンが返る。
/// シェルの部品は、スレッドの種類(STA)を選ぶことがあるため、専用の STA スレッドで順に処理する(画面を止めない)。
/// 取得したものは、メモリに記憶して(最近使ったものから、一定数まで)、別のフォルダへ移って戻ったときに、読み込み直さない。
/// </summary>
public static class ThumbnailService
{
    private sealed record Request(string Path, int Size, long Stamp, TaskCompletionSource<ImageSource?> Result);

    private static readonly BlockingCollection<Request> Queue = new();
    private static int _started;

    /// <summary>項目が頼むサムネイルの大きさ(1辺、ピクセル)。アイコンの大きさの設定に合わせて変わる。</summary>
    public static int RequestSize { get; set; } = 96;

    // ───────── 記憶(最近使ったものを残す) ─────────

    private const int CacheCapacity = 800;
    private static readonly object CacheGate = new();
    private static readonly Dictionary<string, LinkedListNode<(string Key, ImageSource? Image)>> CacheMap = new();
    private static readonly LinkedList<(string Key, ImageSource? Image)> CacheOrder = new();

    // パスと、更新日時(の目印)と、大きさで区別する。ファイルが更新されたら、別のものとして読み直す
    private static string CacheKey(string path, int size, long stamp) => $"{size}|{stamp}|{path}";

    /// <summary>記憶にあれば true(取得に失敗したものも、記憶してあるので、image が null で true になる)。</summary>
    public static bool TryGetCached(string path, int size, long stamp, out ImageSource? image)
    {
        lock (CacheGate)
        {
            if (CacheMap.TryGetValue(CacheKey(path, size, stamp), out var node))
            {
                CacheOrder.Remove(node);
                CacheOrder.AddFirst(node);
                image = node.Value.Image;
                return true;
            }
        }
        image = null;
        return false;
    }

    private static void Remember(string path, int size, long stamp, ImageSource? image)
    {
        string key = CacheKey(path, size, stamp);
        lock (CacheGate)
        {
            if (CacheMap.TryGetValue(key, out var existing))
            {
                CacheOrder.Remove(existing);
                CacheMap.Remove(key);
            }
            var node = CacheOrder.AddFirst((key, image));
            CacheMap[key] = node;
            while (CacheMap.Count > CacheCapacity && CacheOrder.Last != null)
            {
                CacheMap.Remove(CacheOrder.Last.Value.Key);
                CacheOrder.RemoveLast();
            }
        }
    }

    public static void ClearCache()
    {
        lock (CacheGate)
        {
            CacheMap.Clear();
            CacheOrder.Clear();
        }
    }

    // ───────── 取得 ─────────

    /// <summary>path のサムネイル(無ければ大きなアイコン)を、size 四方に収まる大きさで取得する。取得できなければ null。</summary>
    public static Task<ImageSource?> GetAsync(string path, int size) => GetAsync(path, size, 0);

    /// <summary>stamp は、ファイルの更新日時の目印(Ticks など)。記憶の区別に使い、更新されたものは、読み直す。</summary>
    public static Task<ImageSource?> GetAsync(string path, int size, long stamp)
    {
        if (TryGetCached(path, size, stamp, out var cached)) return Task.FromResult(cached);

        EnsureWorkers();
        var request = new Request(path, size, stamp, new TaskCompletionSource<ImageSource?>(TaskCreationOptions.RunContinuationsAsynchronously));
        Queue.Add(request);
        return request.Result.Task;
    }

    private static void EnsureWorkers()
    {
        if (Interlocked.Exchange(ref _started, 1) == 1) return;
        for (int i = 0; i < 3; i++)
        {
            var thread = new Thread(Work) { IsBackground = true, Name = "KT_Explorer thumbnails " + i };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
        }
    }

    private static void Work()
    {
        foreach (var request in Queue.GetConsumingEnumerable())
        {
            ImageSource? image = null;
            try { image = Load(request.Path, request.Size); }
            catch { /* 取得できないものは、小さなアイコンのままにする */ }
            Remember(request.Path, request.Size, request.Stamp, image);
            request.Result.TrySetResult(image);
        }
    }

    // ───────── シェル(IShellItemImageFactory) ─────────

    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        [PreserveSig]
        int GetImage(SIZE size, int flags, out IntPtr phbm);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SIZE
    {
        public int cx;
        public int cy;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SHCreateItemFromParsingName(string path, IntPtr bindContext, ref Guid riid, out IShellItemImageFactory factory);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr obj);

    private static readonly Guid ImageFactoryId = new("bcc18b79-ba16-442f-80c4-8a59c30c463b");

    private const int SIIGBF_BIGGERSIZEOK = 0x1;

    private static ImageSource? Load(string path, int size)
    {
        var iid = ImageFactoryId;
        SHCreateItemFromParsingName(path, IntPtr.Zero, ref iid, out var factory);
        try
        {
            int hr = factory.GetImage(new SIZE { cx = size, cy = size }, SIIGBF_BIGGERSIZEOK, out var hbitmap);
            if (hr != 0 || hbitmap == IntPtr.Zero) return null;
            try
            {
                var source = Imaging.CreateBitmapSourceFromHBitmap(hbitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                source.Freeze();
                return source;
            }
            finally
            {
                DeleteObject(hbitmap);
            }
        }
        finally
        {
            Marshal.ReleaseComObject(factory);
        }
    }
}
