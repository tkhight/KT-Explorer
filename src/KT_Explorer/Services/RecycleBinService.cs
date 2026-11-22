using System.IO;

namespace KT_Explorer.Services;

/// <summary>ごみ箱に入れた項目を、元の場所へ戻す(Shell.Application の「元に戻す」と同じ動作)。</summary>
public static class RecycleBinService
{
    private const int ssfBITBUCKET = 10; // ごみ箱

    /// <summary>
    /// originalPath にあった項目を、ごみ箱から元の場所へ復元する。
    /// 同じパスの項目が複数あるときは、最後に削除したものを戻す。ごみ箱に無ければ false。
    /// </summary>
    public static bool Restore(string originalPath)
    {
        try
        {
            var type = Type.GetTypeFromProgID("Shell.Application");
            if (type == null) return false;
            dynamic shell = Activator.CreateInstance(type)!;
            dynamic? bin = shell.NameSpace(ssfBITBUCKET);
            if (bin == null) return false;

            dynamic items = bin.Items();
            int count = items.Count;

            object? best = null;
            DateTime bestDate = DateTime.MinValue;
            for (int i = 0; i < count; i++)
            {
                try
                {
                    object? entry = items.Item(i);
                    if (entry == null) continue;
                    dynamic item = entry;

                    if (!string.Equals(OriginalPathOf(item), originalPath, StringComparison.OrdinalIgnoreCase)) continue;

                    DateTime deleted = DateTime.MinValue;
                    try { deleted = Convert.ToDateTime((object)item.ExtendedProperty("System.Recycle.DateDeleted")); } catch { /* 日時が取れなければ最古扱い */ }
                    if (best == null || deleted >= bestDate) { best = entry; bestDate = deleted; }
                }
                catch
                {
                    // 読めない項目は飛ばす
                }
            }
            if (best == null) return false;

            ((dynamic)best).InvokeVerb("undelete");

            // 復元は少し遅れて反映されることがある
            for (int i = 0; i < 40; i++)
            {
                if (File.Exists(originalPath) || Directory.Exists(originalPath)) return true;
                Thread.Sleep(50);
            }
            return false;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>ごみ箱の項目から、削除前のフルパスを組み立てる。</summary>
    private static string OriginalPathOf(dynamic item)
    {
        string dir = (string?)item.ExtendedProperty("System.Recycle.DeletedFrom") ?? "";
        string name = (string?)item.Name ?? "";
        string binPath = (string?)item.Path ?? "";

        // 「登録されている拡張子は表示しない」設定だと、名前から拡張子が省かれる。ごみ箱内の実体の拡張子で補う。
        string ext = Path.GetExtension(binPath);
        if (ext.Length > 0 && !name.EndsWith(ext, StringComparison.OrdinalIgnoreCase)) name += ext;
        return Path.Combine(dir, name);
    }
}
