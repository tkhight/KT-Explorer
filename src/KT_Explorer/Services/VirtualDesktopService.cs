using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Threading;

namespace KT_Explorer.Services;

/// <summary>
/// 仮想デスクトップの切替にウィンドウを追従させる。
/// 公開 COM の IVirtualDesktopManager だけを使い、自ウィンドウが現在のデスクトップに居なければ
/// 現在のデスクトップ上のウィンドウのデスクトップ ID へ移動する(ポーリング方式)。
/// </summary>
public sealed class VirtualDesktopService : IDisposable
{
    [ComImport, Guid("a5cd92ff-29be-454c-8d04-d82879fb3f1b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IVirtualDesktopManager
    {
        [PreserveSig] int IsWindowOnCurrentVirtualDesktop(IntPtr topLevelWindow, [MarshalAs(UnmanagedType.Bool)] out bool onCurrent);
        [PreserveSig] int GetWindowDesktopId(IntPtr topLevelWindow, out Guid desktopId);
        [PreserveSig] int MoveWindowToDesktop(IntPtr topLevelWindow, ref Guid desktopId);
    }

    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern IntPtr GetShellWindow();

    private readonly IVirtualDesktopManager? _manager;
    private readonly DispatcherTimer _timer;
    private readonly IntPtr _hwnd;

    public VirtualDesktopService(System.Windows.Window window)
    {
        _hwnd = new WindowInteropHelper(window).EnsureHandle();
        try
        {
            var type = Type.GetTypeFromCLSID(new Guid("aa509086-5ca9-4c25-8f95-589d3c07b48a"));
            if (type != null) _manager = (IVirtualDesktopManager?)Activator.CreateInstance(type);
        }
        catch
        {
            _manager = null; // 取得できない環境では追従なし
        }

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _timer.Tick += (_, _) => Follow();
        if (_manager != null) _timer.Start();
    }

    /// <summary>今すぐ現在のデスクトップへ移動する(トレイから表示するとき用)。</summary>
    public void FollowNow() => Follow();

    private void Follow()
    {
        try
        {
            if (_manager!.IsWindowOnCurrentVirtualDesktop(_hwnd, out bool onCurrent) != 0 || onCurrent) return;

            // 1) レジストリの現在デスクトップ ID(最も確実)
            var current = ReadCurrentDesktopId();
            if (current != Guid.Empty)
            {
                var id = current;
                if (_manager.MoveWindowToDesktop(_hwnd, ref id) == 0) return;
            }

            // 2) フォールバック: 現在のデスクトップ上の他ウィンドウの ID を使う
            foreach (var candidate in new[] { GetForegroundWindow(), GetShellWindow() })
            {
                if (candidate == IntPtr.Zero || candidate == _hwnd) continue;
                if (_manager.IsWindowOnCurrentVirtualDesktop(candidate, out bool ok) != 0 || !ok) continue;
                if (_manager.GetWindowDesktopId(candidate, out Guid id) != 0 || id == Guid.Empty) continue;
                _manager.MoveWindowToDesktop(_hwnd, ref id);
                return;
            }
        }
        catch
        {
            // 一時的な COM 失敗は次の周期で再試行
        }
    }

    private static Guid ReadCurrentDesktopId()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\VirtualDesktops");
            if (key?.GetValue("CurrentVirtualDesktop") is byte[] { Length: >= 16 } bytes)
                return new Guid(bytes.AsSpan(0, 16));
        }
        catch
        {
            // 読めなければフォールバックへ
        }
        return Guid.Empty;
    }

    public void Dispose() => _timer.Stop();
}
