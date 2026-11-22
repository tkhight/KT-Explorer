using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Interop;

namespace KT_Explorer.Services;

/// <summary>
/// Windows のシェルが標準のエクスプローラーで出すコンテキストメニュー(IContextMenu)を表示する。
/// 7-Zip・Git・ウイルス対策などのシェル拡張、「送る」「プログラムから開く」「新規作成」も含む。
/// Windows 11 の新形式メニューは他のアプリからは呼べないため、「その他のオプション」の従来形式のメニューになる。
/// </summary>
public static class ShellContextMenu
{
    private const uint FirstCmd = 1;
    private const uint LastCmd = 0x7FFF;

    private const uint CMF_CANRENAME = 0x10;
    private const uint CMF_EXTENDEDVERBS = 0x100;   // Shift を押したときに出る拡張項目
    private const uint TPM_RIGHTBUTTON = 0x2;
    private const uint TPM_RETURNCMD = 0x100;
    private const uint GCS_VERBW = 4;
    private const uint CMIC_MASK_UNICODE = 0x4000;
    private const uint CMIC_MASK_PTINVOKE = 0x20000000;
    private const int SW_SHOWNORMAL = 1;

    private const int WM_NULL = 0x0;
    private const int WM_DRAWITEM = 0x2B;
    private const int WM_MEASUREITEM = 0x2C;
    private const int WM_INITMENUPOPUP = 0x117;
    private const int WM_MENUCHAR = 0x120;

    private static readonly Guid IID_IShellFolder = new("000214E6-0000-0000-C000-000000000046");
    private static readonly Guid IID_IContextMenu = new("000214E4-0000-0000-C000-000000000046");

    // ───────── COM / Win32 の宣言 ─────────

    [ComImport, Guid("000214E6-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellFolder
    {
        [PreserveSig] int ParseDisplayName(IntPtr hwnd, IntPtr pbc, [MarshalAs(UnmanagedType.LPWStr)] string name, out uint eaten, out IntPtr pidl, ref uint attrs);
        [PreserveSig] int EnumObjects(IntPtr hwnd, int flags, out IntPtr enumIdList);
        [PreserveSig] int BindToObject(IntPtr pidl, IntPtr pbc, ref Guid riid, out IntPtr ppv);
        [PreserveSig] int BindToStorage(IntPtr pidl, IntPtr pbc, ref Guid riid, out IntPtr ppv);
        [PreserveSig] int CompareIDs(IntPtr lParam, IntPtr pidl1, IntPtr pidl2);
        [PreserveSig] int CreateViewObject(IntPtr hwndOwner, ref Guid riid, out IntPtr ppv);
        [PreserveSig] int GetAttributesOf(uint cidl, [MarshalAs(UnmanagedType.LPArray)] IntPtr[] apidl, ref uint attrs);
        [PreserveSig] int GetUIObjectOf(IntPtr hwndOwner, uint cidl, [MarshalAs(UnmanagedType.LPArray)] IntPtr[] apidl, ref Guid riid, IntPtr reserved, out IntPtr ppv);
        [PreserveSig] int GetDisplayNameOf(IntPtr pidl, uint flags, IntPtr name);
        [PreserveSig] int SetNameOf(IntPtr hwnd, IntPtr pidl, [MarshalAs(UnmanagedType.LPWStr)] string name, uint flags, out IntPtr pidlOut);
    }

    [ComImport, Guid("000214E4-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IContextMenu
    {
        [PreserveSig] int QueryContextMenu(IntPtr hmenu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint flags);
        [PreserveSig] int InvokeCommand(ref CMINVOKECOMMANDINFOEX info);
        [PreserveSig] int GetCommandString(UIntPtr idCmd, uint type, IntPtr reserved, IntPtr name, uint cchMax);
    }

    [ComImport, Guid("000214F4-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IContextMenu2
    {
        [PreserveSig] int QueryContextMenu(IntPtr hmenu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint flags);
        [PreserveSig] int InvokeCommand(ref CMINVOKECOMMANDINFOEX info);
        [PreserveSig] int GetCommandString(UIntPtr idCmd, uint type, IntPtr reserved, IntPtr name, uint cchMax);
        [PreserveSig] int HandleMenuMsg(uint msg, IntPtr wParam, IntPtr lParam);
    }

    [ComImport, Guid("BCFCE0A0-EC17-11D0-8D10-00A0C90F2719"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IContextMenu3
    {
        [PreserveSig] int QueryContextMenu(IntPtr hmenu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint flags);
        [PreserveSig] int InvokeCommand(ref CMINVOKECOMMANDINFOEX info);
        [PreserveSig] int GetCommandString(UIntPtr idCmd, uint type, IntPtr reserved, IntPtr name, uint cchMax);
        [PreserveSig] int HandleMenuMsg(uint msg, IntPtr wParam, IntPtr lParam);
        [PreserveSig] int HandleMenuMsg2(uint msg, IntPtr wParam, IntPtr lParam, out IntPtr result);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct CMINVOKECOMMANDINFOEX
    {
        public uint cbSize;
        public uint fMask;
        public IntPtr hwnd;
        public IntPtr lpVerb;
        public IntPtr lpParameters;
        public IntPtr lpDirectory;
        public int nShow;
        public uint dwHotKey;
        public IntPtr hIcon;
        public IntPtr lpTitle;
        public IntPtr lpVerbW;
        public IntPtr lpParametersW;
        public IntPtr lpDirectoryW;
        public IntPtr lpTitleW;
        public POINT ptInvoke;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHParseDisplayName(string name, IntPtr bindCtx, out IntPtr pidl, uint sfgaoIn, out uint sfgaoOut);

    [DllImport("shell32.dll")]
    private static extern int SHBindToParent(IntPtr pidl, ref Guid riid, out IntPtr ppv, out IntPtr pidlLast);

    [DllImport("user32.dll")] private static extern IntPtr CreatePopupMenu();
    [DllImport("user32.dll")] private static extern bool DestroyMenu(IntPtr hMenu);
    [DllImport("user32.dll")] private static extern int GetMenuItemCount(IntPtr hMenu);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetMenuStringW(IntPtr hMenu, uint item, StringBuilder text, int max, uint flags);
    [DllImport("user32.dll")] private static extern uint TrackPopupMenuEx(IntPtr hMenu, uint flags, int x, int y, IntPtr hwnd, IntPtr tpm);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT pt);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    // ───────── 表示 ─────────

    /// <summary>
    /// シェルのコンテキストメニューをカーソル位置に出して、選ばれた項目を実行する。
    /// </summary>
    /// <param name="hwnd">メニューの所有ウィンドウ</param>
    /// <param name="paths">対象の項目(同じフォルダ内のものだけ)。背景メニューのときは空</param>
    /// <param name="backgroundFolder">フォルダの空白部分のメニューを出すときのフォルダ。項目のメニューなら null</param>
    /// <param name="extended">Shift 押下時の拡張項目も含める</param>
    /// <param name="intercept">選ばれた動詞("open"、"rename" など)を受け取り、true を返すとシェルには実行させない。
    /// 標準のエクスプローラーの画面(IShellView)を前提とする動詞は、KT_Explorer 自身の処理に置き換えるために使う</param>
    public static void Show(IntPtr hwnd, IReadOnlyList<string> paths, string? backgroundFolder, bool extended, Func<string, bool> intercept)
    {
        using var session = Session.Create(hwnd, paths, backgroundFolder, extended);

        // 「送る」などのサブメニューや、アイコン付きの項目は、ウィンドウメッセージを転送しないと正しく描画されない
        var source = HwndSource.FromHwnd(hwnd);
        IntPtr Hook(IntPtr h, int msg, IntPtr w, IntPtr l, ref bool handled)
        {
            if (msg != WM_INITMENUPOPUP && msg != WM_DRAWITEM && msg != WM_MEASUREITEM && msg != WM_MENUCHAR) return IntPtr.Zero;
            try
            {
                if (session.Menu3 != null)
                {
                    session.Menu3.HandleMenuMsg2((uint)msg, w, l, out var result);
                    handled = true;
                    return result;
                }
                if (session.Menu2 != null)
                {
                    session.Menu2.HandleMenuMsg((uint)msg, w, l);
                    handled = true;
                }
            }
            catch
            {
                // 拡張側の失敗でメニュー全体を巻き込まない
            }
            return IntPtr.Zero;
        }

        uint command;
        GetCursorPos(out var pt);
        source?.AddHook(Hook);
        try
        {
            SetForegroundWindow(hwnd); // メニューの外をクリックしたときに閉じるために必要
            command = TrackPopupMenuEx(session.HMenu, TPM_RIGHTBUTTON | TPM_RETURNCMD, pt.X, pt.Y, hwnd, IntPtr.Zero);
            PostMessage(hwnd, WM_NULL, IntPtr.Zero, IntPtr.Zero);
        }
        finally
        {
            source?.RemoveHook(Hook);
        }
        if (command == 0) return; // 取り消し

        uint offset = command - FirstCmd;
        string? verb = GetVerb(session.Menu, offset);
        if (verb != null && intercept(verb)) return;

        var info = new CMINVOKECOMMANDINFOEX
        {
            cbSize = (uint)Marshal.SizeOf<CMINVOKECOMMANDINFOEX>(),
            fMask = CMIC_MASK_UNICODE | CMIC_MASK_PTINVOKE,
            hwnd = hwnd,
            lpVerb = (IntPtr)offset,   // MAKEINTRESOURCE(offset)
            lpVerbW = (IntPtr)offset,
            nShow = SW_SHOWNORMAL,
            ptInvoke = pt,
        };
        Check(session.Menu.InvokeCommand(ref info), "コマンドの実行");
    }

    /// <summary>メニュー項目の動詞("open"、"copy"、"properties" など。言語に依存しない名前)を得る。無ければ null。</summary>
    private static string? GetVerb(IContextMenu menu, uint offset)
    {
        IntPtr buffer = Marshal.AllocCoTaskMem(1024);
        try
        {
            Marshal.WriteInt16(buffer, 0, 0);
            int hr = menu.GetCommandString((UIntPtr)offset, GCS_VERBW, IntPtr.Zero, buffer, 256);
            if (hr < 0) return null;
            string? verb = Marshal.PtrToStringUni(buffer);
            return string.IsNullOrEmpty(verb) ? null : verb;
        }
        catch
        {
            return null;
        }
        finally
        {
            Marshal.FreeCoTaskMem(buffer);
        }
    }

    /// <summary>メニューの最上位の項目名を返す(動作確認用。表示はしない)。</summary>
    internal static List<string> ListMenuLabels(IntPtr hwnd, IReadOnlyList<string> paths, string? backgroundFolder, bool extended = false)
    {
        using var session = Session.Create(hwnd, paths, backgroundFolder, extended);
        var labels = new List<string>();
        int count = GetMenuItemCount(session.HMenu);
        for (int i = 0; i < count; i++)
        {
            var sb = new StringBuilder(256);
            GetMenuStringW(session.HMenu, (uint)i, sb, sb.Capacity, 0x400 /* MF_BYPOSITION */);
            labels.Add(sb.ToString());
        }
        return labels;
    }

    private static void Check(int hr, string what)
    {
        if (hr < 0) throw new COMException($"{what}に失敗しました(0x{hr:X8})。", hr);
    }

    // ───────── メニューの組み立て ─────────

    /// <summary>1回分のメニュー(COM オブジェクトとハンドル)。破棄時にすべて解放する。</summary>
    private sealed class Session : IDisposable
    {
        private readonly List<IntPtr> _pidls = new();
        private IShellFolder? _folder;

        public IContextMenu Menu { get; private set; } = null!;
        public IContextMenu2? Menu2 { get; private set; }
        public IContextMenu3? Menu3 { get; private set; }
        public IntPtr HMenu { get; private set; }

        public static Session Create(IntPtr hwnd, IReadOnlyList<string> paths, string? backgroundFolder, bool extended)
        {
            var s = new Session();
            try
            {
                s.Build(hwnd, paths, backgroundFolder, extended);
                return s;
            }
            catch
            {
                s.Dispose();
                throw;
            }
        }

        private IntPtr Parse(string path)
        {
            Check(SHParseDisplayName(path, IntPtr.Zero, out var pidl, 0, out _), $"「{path}」の取得");
            _pidls.Add(pidl);
            return pidl;
        }

        private static IShellFolder BindToParent(IntPtr pidl, out IntPtr last)
        {
            var iid = IID_IShellFolder;
            Check(SHBindToParent(pidl, ref iid, out var ppv, out last), "親フォルダの取得");
            var folder = (IShellFolder)Marshal.GetObjectForIUnknown(ppv);
            Marshal.Release(ppv);
            return folder;
        }

        private void Build(IntPtr hwnd, IReadOnlyList<string> paths, string? backgroundFolder, bool extended)
        {
            var iidMenu = IID_IContextMenu;
            IntPtr ppv;

            if (backgroundFolder != null)
            {
                // フォルダの空白部分のメニュー: そのフォルダ自身のビューオブジェクトから得る
                var parent = BindToParent(Parse(backgroundFolder), out var last);
                var iidFolder = IID_IShellFolder;
                Check(parent.BindToObject(last, IntPtr.Zero, ref iidFolder, out var pf), "フォルダを開く");
                _folder = (IShellFolder)Marshal.GetObjectForIUnknown(pf);
                Marshal.Release(pf);
                Marshal.ReleaseComObject(parent);
                Check(_folder.CreateViewObject(hwnd, ref iidMenu, out ppv), "メニューの作成");
            }
            else
            {
                if (paths.Count == 0) throw new ArgumentException("対象がありません。", nameof(paths));

                // 項目のメニュー: 同じ親フォルダの子として、まとめて IContextMenu を得る
                var children = new IntPtr[paths.Count];
                for (int i = 0; i < paths.Count; i++)
                {
                    var p = BindToParent(Parse(paths[i]), out children[i]);
                    if (i == 0) _folder = p; else Marshal.ReleaseComObject(p);
                }
                Check(_folder!.GetUIObjectOf(hwnd, (uint)children.Length, children, ref iidMenu, IntPtr.Zero, out ppv), "メニューの作成");
            }

            Menu = (IContextMenu)Marshal.GetObjectForIUnknown(ppv);
            Marshal.Release(ppv);
            Menu2 = Menu as IContextMenu2;
            Menu3 = Menu as IContextMenu3;

            HMenu = CreatePopupMenu();
            uint flags = CMF_CANRENAME | (extended ? CMF_EXTENDEDVERBS : 0);
            Check(Menu.QueryContextMenu(HMenu, 0, FirstCmd, LastCmd, flags), "メニュー項目の取得");
        }

        public void Dispose()
        {
            if (HMenu != IntPtr.Zero) { DestroyMenu(HMenu); HMenu = IntPtr.Zero; }
            if (Menu != null) { try { Marshal.ReleaseComObject(Menu); } catch { /* 解放済み */ } }
            if (_folder != null) { try { Marshal.ReleaseComObject(_folder); } catch { /* 解放済み */ } }
            foreach (var pidl in _pidls) Marshal.FreeCoTaskMem(pidl);
            _pidls.Clear();
        }
    }
}
