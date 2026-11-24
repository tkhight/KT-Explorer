using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace KT_Explorer.Services;

/// <summary>1つの項目に重ねるオーバーレイアイコン(小: 16px 用 / 大: アイコン表示用)。</summary>
public sealed record OverlayIcons(ImageSource Small, ImageSource Large);

/// <summary>
/// シェルのオーバーレイアイコン(OneDrive の同期状態、TortoiseSVN の状態、ショートカットの矢印など)の取得。
/// エクスプローラーと同じ、シェルのオーバーレイ ハンドラー(ShellIconOverlayIdentifiers)が決める。
/// ハンドラーは遅いことがあり、シェルの部品はスレッドの種類(STA)を選ぶことがあるため、専用の STA スレッドで順に処理する(画面を止めない)。
/// オーバーレイの画像そのものは、種類(番号)ごとに1度だけ作って使い回す。
/// </summary>
public static class OverlayIconService
{
    /// <summary>オーバーレイを表示するか(「表示」メニューの設定)。オフの間は、シェルに問い合わせない。</summary>
    public static bool Enabled { get; set; } = true;

    private sealed record Request(string Path, TaskCompletionSource<OverlayIcons?> Result);

    private static readonly BlockingCollection<Request> Queue = new();
    private static int _started;

    /// <summary>path のオーバーレイを取得する。オーバーレイが無い(または、オフ・取得できない)ときは null。</summary>
    public static Task<OverlayIcons?> GetAsync(string path)
    {
        if (!Enabled) return Task.FromResult<OverlayIcons?>(null);
        EnsureWorker();
        var request = new Request(path, new TaskCompletionSource<OverlayIcons?>(TaskCreationOptions.RunContinuationsAsynchronously));
        Queue.Add(request);
        return request.Result.Task;
    }

    private static void EnsureWorker()
    {
        if (Interlocked.Exchange(ref _started, 1) == 1) return;
        for (int i = 0; i < 2; i++)
        {
            var thread = new Thread(Work) { IsBackground = true, Name = "KT_Explorer overlays " + i };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
        }
    }

    private static void Work()
    {
        foreach (var request in Queue.GetConsumingEnumerable())
        {
            OverlayIcons? result = null;
            try { result = Load(request.Path); }
            catch { /* ハンドラーの失敗: オーバーレイ無しとして扱う */ }
            request.Result.TrySetResult(result);
        }
    }

    // ───────── シェル ─────────

    private const uint SHGFI_ICON = 0x100;
    private const uint SHGFI_SYSICONINDEX = 0x4000;
    private const uint SHGFI_SMALLICON = 0x1;
    private const uint SHGFI_OVERLAYINDEX = 0x40;
    private const int SHIL_SMALL = 1;
    private const int SHIL_EXTRALARGE = 2;
    private const int ILD_TRANSPARENT = 0x1;

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

    [DllImport("shell32.dll", PreserveSig = false)]
    private static extern void SHGetImageList(int iImageList, ref Guid riid, out IImageList ppv);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);

    private static readonly Guid ImageListId = new("46EB5926-582E-4017-9FDF-E8998DAA0950");

    /// <summary>
    /// システムのイメージ リスト(IImageList)。呼び出すのは GetIcon と GetOverlayImage だけだが、
    /// COM は定義の並び順で呼ぶので、途中のメソッドも、順番どおりに宣言しておく必要がある。
    /// </summary>
    [ComImport, Guid("46EB5926-582E-4017-9FDF-E8998DAA0950"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IImageList
    {
        [PreserveSig] int Add();
        [PreserveSig] int ReplaceIcon();
        [PreserveSig] int SetOverlayImage();
        [PreserveSig] int Replace();
        [PreserveSig] int AddMasked();
        [PreserveSig] int Draw();
        [PreserveSig] int Remove();
        [PreserveSig] int GetIcon(int i, int flags, out IntPtr picon);
        [PreserveSig] int GetImageInfo();
        [PreserveSig] int Copy();
        [PreserveSig] int Merge();
        [PreserveSig] int Clone();
        [PreserveSig] int GetImageRect();
        [PreserveSig] int GetIconSize();
        [PreserveSig] int SetIconSize();
        [PreserveSig] int GetImageCount();
        [PreserveSig] int SetImageCount();
        [PreserveSig] int SetBkColor();
        [PreserveSig] int GetBkColor();
        [PreserveSig] int BeginDrag();
        [PreserveSig] int EndDrag();
        [PreserveSig] int DragEnter();
        [PreserveSig] int DragLeave();
        [PreserveSig] int DragMove();
        [PreserveSig] int SetDragCursorImage();
        [PreserveSig] int DragShowNolock();
        [PreserveSig] int GetDragImage();
        [PreserveSig] int GetItemFlags();
        [PreserveSig] int GetOverlayImage(int iOverlay, out int piIndex);
    }

    // オーバーレイの番号(1 〜)ごとの画像。取得できなかった番号も、null で記憶する
    private static readonly ConcurrentDictionary<int, OverlayIcons?> ByIndex = new();

    /// <summary>path のオーバーレイの番号(1 〜)。無ければ 0。</summary>
    internal static int GetOverlayIndex(string path)
    {
        var info = new SHFILEINFO();
        // SHGFI_OVERLAYINDEX は、SHGFI_ICON と SHGFI_SYSICONINDEX が必要。結果は iIcon の上位 8 ビット。実在するファイル・フォルダで調べる(ハンドラーが状態を見る)
        SHGetFileInfo(path, 0, ref info, (uint)Marshal.SizeOf<SHFILEINFO>(), SHGFI_ICON | SHGFI_SYSICONINDEX | SHGFI_OVERLAYINDEX | SHGFI_SMALLICON);
        if (info.hIcon != IntPtr.Zero) DestroyIcon(info.hIcon); // アイコンそのものは使わない
        return (info.iIcon >> 24) & 0xFF;
    }

    private static OverlayIcons? Load(string path)
    {
        int overlay = GetOverlayIndex(path);
        if (overlay <= 0) return null;
        return ByIndex.GetOrAdd(overlay, BuildImages);
    }

    private static OverlayIcons? BuildImages(int overlay)
    {
        var small = BuildImage(SHIL_SMALL, overlay);
        var large = BuildImage(SHIL_EXTRALARGE, overlay);
        if (small == null && large == null) return null;
        return new OverlayIcons(small ?? large!, large ?? small!);
    }

    private static ImageSource? BuildImage(int imageListKind, int overlay)
    {
        var iid = ImageListId;
        SHGetImageList(imageListKind, ref iid, out var list);
        try
        {
            if (list.GetOverlayImage(overlay, out int imageIndex) != 0 || imageIndex < 0) return null;
            if (list.GetIcon(imageIndex, ILD_TRANSPARENT, out var hIcon) != 0 || hIcon == IntPtr.Zero) return null;
            try
            {
                var source = Imaging.CreateBitmapSourceFromHIcon(hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                source.Freeze();
                return source;
            }
            finally
            {
                DestroyIcon(hIcon);
            }
        }
        finally
        {
            Marshal.ReleaseComObject(list);
        }
    }
}
