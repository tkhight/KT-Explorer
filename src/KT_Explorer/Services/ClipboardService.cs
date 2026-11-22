using System.Collections.Specialized;
using System.IO;
using System.Windows;

namespace KT_Explorer.Services;

/// <summary>
/// ファイルのコピー/切り取りを Windows のクリップボードで行う。
/// 標準のエクスプローラーとも相互にコピー&ペーストできる("Preferred DropEffect" で切り取りかコピーかを区別)。
/// </summary>
public static class ClipboardService
{
    private const string PreferredDropEffect = "Preferred DropEffect";
    private const int DropEffectCopy = 1;
    private const int DropEffectMove = 2;

    /// <summary>paths をクリップボードに入れる。cut=true なら「切り取り」。失敗したら false。</summary>
    public static bool SetFiles(IEnumerable<string> paths, bool cut)
    {
        try
        {
            var list = new StringCollection();
            foreach (var p in paths) list.Add(p);

            var data = new DataObject();
            data.SetFileDropList(list);
            data.SetData(PreferredDropEffect, new MemoryStream(BitConverter.GetBytes(cut ? DropEffectMove : DropEffectCopy)));
            Clipboard.SetDataObject(data, copy: true);
            return true;
        }
        catch
        {
            return false; // 他のアプリがクリップボードを使用中など
        }
    }

    /// <summary>クリップボードにファイルがあれば取り出す。無ければ null。</summary>
    public static (List<string> Paths, bool Cut)? GetFiles()
    {
        try
        {
            if (!Clipboard.ContainsFileDropList()) return null;
            var paths = Clipboard.GetFileDropList().Cast<string>().ToList();
            if (paths.Count == 0) return null;

            bool cut = false;
            if (Clipboard.GetDataObject()?.GetData(PreferredDropEffect) is MemoryStream ms && ms.Length >= 4)
            {
                var bytes = ms.ToArray();
                cut = (BitConverter.ToInt32(bytes, 0) & DropEffectMove) != 0;
            }
            return (paths, cut);
        }
        catch
        {
            return null;
        }
    }

    public static bool HasFiles()
    {
        try { return Clipboard.ContainsFileDropList(); }
        catch { return false; }
    }

    public static void Clear()
    {
        try { Clipboard.Clear(); } catch { /* 使用中なら残す */ }
    }
}
