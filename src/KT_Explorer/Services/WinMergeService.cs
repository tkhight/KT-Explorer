using System.Diagnostics;
using System.IO;

namespace KT_Explorer.Services;

/// <summary>外部の差分ツール WinMerge を呼び出す。</summary>
public static class WinMergeService
{
    /// <summary>WinMerge の実行ファイル(WinMergeU.exe)。見つからなければ null。</summary>
    public static string? FindExecutable()
    {
        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs"),
        };
        foreach (var root in roots)
        {
            if (string.IsNullOrEmpty(root)) continue;
            string exe = Path.Combine(root, "WinMerge", "WinMergeU.exe");
            if (File.Exists(exe)) return exe;
        }
        return null;
    }

    /// <summary>
    /// 比較に使う引数を作る。0 個なら WinMerge だけを起動、1 個なら、そのパスを左側に開く、2 個なら、2 つを比較する。
    /// 2 つは、ファイル同士・フォルダー同士のどちらかで、混ぜられない(混ぜたときは null)。
    /// </summary>
    public static IReadOnlyList<string>? BuildArguments(IReadOnlyList<string> paths)
    {
        if (paths.Count > 2) return null;
        if (paths.Count == 2 && Directory.Exists(paths[0]) != Directory.Exists(paths[1])) return null;
        var args = new List<string>();
        if (paths.Count > 0) args.Add("/e"); // 比較したら、Esc で閉じられる
        args.AddRange(paths);
        return args;
    }

    /// <summary>WinMerge を起動する。起動できれば true。</summary>
    public static bool Launch(IReadOnlyList<string> arguments, out string? error)
    {
        error = null;
        string? exe = FindExecutable();
        if (exe == null) { error = "WinMerge が見つかりません(Program Files\\WinMerge にインストールしてください)。"; return false; }
        try
        {
            var info = new ProcessStartInfo(exe) { UseShellExecute = false };
            foreach (var a in arguments) info.ArgumentList.Add(a);
            Process.Start(info);
            return true;
        }
        catch (Exception ex) { error = ex.Message; return false; }
    }
}
