using System.IO;
using KT_Explorer.ViewModels;

namespace KT_Explorer.Services;

/// <summary>「指定ペイン」で、まとめて作るペインの条件(2〜3 つ)と、フォルダの入力の整理。</summary>
public static class PaneRequest
{
    public const int Min = 2;
    public const int Max = 3;

    /// <summary>ペインの数として、作れる数か(2 か 3)。</summary>
    public static bool IsValidCount(int count) => count >= Min && count <= Max;

    /// <summary>
    /// フォルダ指定の入力欄(空欄を含む)を整理する。空欄は数えない。入力のあるものは、前後の空白と引用符を外して、
    /// 存在するフォルダなら Paths に、存在しなければ InvalidSlots(入力欄の番号。0 から)に入れる。
    /// </summary>
    public static (List<string> Paths, List<int> InvalidSlots) CleanFolders(IEnumerable<string?> texts)
    {
        var paths = new List<string>();
        var invalid = new List<int>();
        int slot = 0;
        foreach (var raw in texts)
        {
            string text = (raw ?? "").Trim().Trim('"').Trim();
            if (text.Length > 0)
            {
                if (Directory.Exists(text)) paths.Add(FolderTabViewModel.Normalize(text));
                else invalid.Add(slot);
            }
            slot++;
        }
        return (paths, invalid);
    }

    /// <summary>「作成」を押せるか: 入力のあるフォルダがすべて存在し、その数が 2 か 3。</summary>
    public static bool CanCreateFromFolders(IEnumerable<string?> texts)
    {
        var (paths, invalid) = CleanFolders(texts);
        return invalid.Count == 0 && IsValidCount(paths.Count);
    }
}
