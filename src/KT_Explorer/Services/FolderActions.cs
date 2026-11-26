using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace KT_Explorer.Services;

/// <summary>フォルダ・ファイルへの小さな操作(一覧とツリーの右クリックメニューで共用する)。</summary>
public static class FolderActions
{
    /// <summary>
    /// Windows のプロパティ画面を出す(ファイル・フォルダ・ドライブ)。
    /// Process.Start の "properties" 動詞は、この画面に必要な指定(SEE_MASK_INVOKEIDLIST)を渡さないので、使わない。
    /// </summary>
    public static void ShowProperties(string path)
    {
        var window = Application.Current?.MainWindow;
        var owner = window == null ? IntPtr.Zero : new WindowInteropHelper(window).Handle;
        if (!SHObjectProperties(owner, SHOP_FILEPATH, path, null))
            MessageBox.Show($"プロパティを表示できませんでした。\n{path}", "KT_Explorer", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private const uint SHOP_FILEPATH = 0x2;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SHObjectProperties(IntPtr hwnd, uint shopObjectType, string pszObjectName, string? pszPropertyPage);

    public static void CopyPathText(IEnumerable<string> paths)
    {
        try { Clipboard.SetText(string.Join(Environment.NewLine, paths)); }
        catch { /* クリップボード使用中 */ }
    }

    /// <summary>そのフォルダで、ターミナルを開く。Windows Terminal があればそれを、なければ PowerShell を使う。</summary>
    public static void OpenTerminal(string folder)
    {
        if (!Directory.Exists(folder)) return;
        try
        {
            Process.Start(new ProcessStartInfo("wt.exe") { ArgumentList = { "-d", folder }, UseShellExecute = true });
            return;
        }
        catch { /* Windows Terminal が無い */ }

        try
        {
            Process.Start(new ProcessStartInfo("powershell.exe") { WorkingDirectory = folder, ArgumentList = { "-NoExit" }, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"ターミナルを開けませんでした。\n{ex.Message}", "KT_Explorer", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>クリップボードにあるファイルを、指定フォルダへ貼り付ける。貼り付けられるものが無ければ false。</summary>
    public static bool PasteInto(string destDir, IntPtr owner)
    {
        var clip = ClipboardService.GetFiles();
        if (clip == null || !Directory.Exists(destDir)) return false;
        var (paths, cut) = clip.Value;

        if (paths.Any(s => Directory.Exists(s) && FileOperationService.IsInsideOrSame(s, destDir)))
        {
            MessageBox.Show("フォルダを、そのフォルダ自身の中へはコピー・移動できません。", "KT_Explorer",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return true;
        }
        if (cut) // 切り取り→同じ場所へは何もしない
            paths = paths.Where(p => !string.Equals(Path.GetDirectoryName(p.TrimEnd('\\')), destDir.TrimEnd('\\'),StringComparison.OrdinalIgnoreCase)).ToList();
        if (paths.Count == 0) return true;

        var result = FileOperationService.Transfer(paths, destDir, move: cut, owner);
        if (result == FileOperationService.Result.Failed)
            MessageBox.Show("一部の操作を完了できませんでした。", "KT_Explorer", MessageBoxButton.OK, MessageBoxImage.Warning);
        if (cut && result == FileOperationService.Result.Done) ClipboardService.Clear();
        return true;
    }
}
