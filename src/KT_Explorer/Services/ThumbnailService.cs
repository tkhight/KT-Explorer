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
/// </summary>
public static class ThumbnailService
{
    private sealed record Request(string Path, int Size, TaskCompletionSource<ImageSource?> Result);

    private static readonly BlockingCollection<Request> Queue = new();
    private static int _started;

    /// <summary>path のサムネイル(無ければ大きなアイコン)を、size 四方に収まる大きさで取得する。取得できなければ null。</summary>
    public static Task<ImageSource?> GetAsync(string path, int size)
    {
        EnsureWorkers();
        var request = new Request(path, size, new TaskCompletionSource<ImageSource?>(TaskCreationOptions.RunContinuationsAsynchronously));
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
