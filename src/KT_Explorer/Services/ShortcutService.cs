using System.IO;
using System.Runtime.InteropServices;

namespace KT_Explorer.Services;

/// <summary>ショートカット(.lnk)のリンク先を調べる。リンク先がフォルダなら、KT_Explorer の中で開けるようにするために使う。</summary>
public static class ShortcutService
{
    /// <summary>拡張子から、ショートカットかどうか(中身は読まない)。</summary>
    public static bool IsShortcut(string path) =>
        string.Equals(Path.GetExtension(path), ".lnk", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// ショートカットのリンク先が、存在するフォルダなら、そのパスを返す。
    /// ファイルへのリンク、リンク先が無い、シェルの特別な項目(コントロールパネルなど、パスを持たないもの)は false。
    /// </summary>
    public static bool TryResolveFolder(string shortcutPath, out string folder)
    {
        folder = "";
        if (!IsShortcut(shortcutPath) || !File.Exists(shortcutPath)) return false;

        string? target = ReadTarget(shortcutPath);
        if (string.IsNullOrWhiteSpace(target)) return false;

        try
        {
            string full = Path.GetFullPath(Environment.ExpandEnvironmentVariables(target));
            if (!Directory.Exists(full)) return false;
            folder = full;
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>ショートカットのリンク先(TargetPath)を読む。読めなければ null。</summary>
    private static string? ReadTarget(string shortcutPath)
    {
        object? shell = null;
        object? link = null;
        try
        {
            var type = Type.GetTypeFromProgID("WScript.Shell");
            if (type == null) return null;
            shell = Activator.CreateInstance(type);
            dynamic wsh = shell!;
            link = wsh.CreateShortcut(shortcutPath);
            dynamic sc = link!;
            return (string?)sc.TargetPath;
        }
        catch
        {
            return null;
        }
        finally
        {
            if (link != null && Marshal.IsComObject(link)) Marshal.ReleaseComObject(link);
            if (shell != null && Marshal.IsComObject(shell)) Marshal.ReleaseComObject(shell);
        }
    }
}
