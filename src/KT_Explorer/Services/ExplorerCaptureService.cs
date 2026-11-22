using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Threading;

namespace KT_Explorer.Services;

/// <summary>
/// 新しく開かれた Windows エクスプローラーのウィンドウを検知し、そのフォルダを取り込んでウィンドウを閉じる。
/// 検知は SetWinEventHook、パス取得は Shell.Application(COM)。
/// 通常のフォルダ以外(PC、ごみ箱、検索結果など)や、複数タブのウィンドウは取り込まず、そのまま残す。
/// </summary>
public sealed class ExplorerCaptureService : IDisposable
{
    private delegate void WinEventDelegate(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);

    [DllImport("user32.dll")]
    private static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr hmod, WinEventDelegate proc, uint pid, uint tid, uint flags);
    [DllImport("user32.dll")] private static extern bool UnhookWinEvent(IntPtr hook);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hwnd, StringBuilder sb, int max);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    private const uint EVENT_OBJECT_CREATE = 0x8000;
    private const uint EVENT_OBJECT_SHOW = 0x8002;
    private const uint WINEVENT_OUTOFCONTEXT = 0;
    private const uint WM_CLOSE = 0x0010;
    private const int MaxAttempts = 25;           // 25 × 120ms ≒ 3秒
    private static readonly TimeSpan RetryInterval = TimeSpan.FromMilliseconds(120);

    private readonly WinEventDelegate _callback; // GC されないよう保持する
    private readonly HashSet<IntPtr> _seen = new();
    private IntPtr _hook = IntPtr.Zero;

    /// <summary>フォルダを取り込んだ(Explorer ウィンドウは既に閉じ始めている)。UI スレッドで発火。</summary>
    public event Action<string>? FolderCaptured;

    public ExplorerCaptureService() => _callback = OnWinEvent;

    public bool Enabled
    {
        get => _hook != IntPtr.Zero;
        set
        {
            if (value && _hook == IntPtr.Zero)
            {
                // UI スレッドのメッセージループ経由でコールバックされる(OUTOFCONTEXT)
                _hook = SetWinEventHook(EVENT_OBJECT_CREATE, EVENT_OBJECT_SHOW, IntPtr.Zero, _callback, 0, 0, WINEVENT_OUTOFCONTEXT);
            }
            else if (!value && _hook != IntPtr.Zero)
            {
                UnhookWinEvent(_hook);
                _hook = IntPtr.Zero;
            }
        }
    }

    private void OnWinEvent(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (idObject != 0 || idChild != 0) return;                 // ウィンドウ自身のイベントだけ
        if (eventType != EVENT_OBJECT_CREATE && eventType != EVENT_OBJECT_SHOW) return;
        if (hwnd == IntPtr.Zero || !IsExplorerWindow(hwnd) || !_seen.Add(hwnd)) return;

        // 作成直後はまだフォルダが決まっていないので、解決できるまで短い間隔で再試行する
        int attempts = 0;
        var timer = new DispatcherTimer { Interval = RetryInterval };
        timer.Tick += (_, _) =>
        {
            attempts++;
            if (!IsWindow(hwnd)) { Finish(timer, hwnd); return; }

            var result = TryResolve(hwnd);
            if (result.Path != null)
            {
                Finish(timer, hwnd);
                PostMessage(hwnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
                FolderCaptured?.Invoke(result.Path);
            }
            else if (result.GiveUp || attempts >= MaxAttempts)
            {
                Finish(timer, hwnd); // 取り込めないウィンドウはそのまま残す
            }
        };
        timer.Start();
    }

    private void Finish(DispatcherTimer timer, IntPtr hwnd)
    {
        timer.Stop();
        // 破棄済みハンドルの再利用に備え、少し待ってから記録を消す
        var cleanup = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        cleanup.Tick += (_, _) => { cleanup.Stop(); _seen.Remove(hwnd); };
        cleanup.Start();
    }

    private static bool IsExplorerWindow(IntPtr hwnd)
    {
        var sb = new StringBuilder(64);
        return GetClassName(hwnd, sb, sb.Capacity) > 0 && sb.ToString() == "CabinetWClass";
    }

    /// <summary>
    /// hwnd の Explorer ウィンドウが表示しているフォルダを得る。
    /// Path が null かつ GiveUp=false なら「まだ決まっていない」ので再試行、GiveUp=true なら取り込み対象外。
    /// </summary>
    private static (string? Path, bool GiveUp) TryResolve(IntPtr hwnd)
    {
        try
        {
            var type = Type.GetTypeFromProgID("Shell.Application");
            if (type == null) return (null, true);
            dynamic shell = Activator.CreateInstance(type)!;
            dynamic windows = shell.Windows();

            string? url = null;
            int matches = 0;
            int count = windows.Count;
            for (int i = 0; i < count; i++)
            {
                try
                {
                    object? item = windows.Item(i);
                    if (item == null) continue;
                    dynamic w = item;
                    if (Convert.ToInt64((object)w.HWND) != hwnd.ToInt64()) continue;
                    matches++;
                    url = (string?)w.LocationURL;
                }
                catch
                {
                    // 他のウィンドウの取得失敗は無視
                }
            }

            if (matches == 0) return (null, false);   // まだ登録されていない
            if (matches > 1) return (null, true);     // 複数タブのウィンドウは取り込まない
            if (string.IsNullOrEmpty(url)) return (null, false);

            if (url.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
            {
                string path = new Uri(url).LocalPath;
                return Directory.Exists(path) ? (path, false) : (null, true);
            }
            return (null, true); // shell:: 系の特殊フォルダなど
        }
        catch
        {
            return (null, false);
        }
    }

    public void Dispose() => Enabled = false;
}
