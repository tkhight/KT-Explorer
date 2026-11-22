using System.IO;
using System.Runtime.InteropServices;

namespace KT_Explorer.Services;

/// <summary>
/// ファイル操作。コピー・移動・削除は Windows 標準の SHFileOperation を使うので、
/// 進捗ダイアログ、上書き確認、ごみ箱、(エクスプローラーでの)元に戻す が標準どおりに働く。
/// </summary>
public static class FileOperationService
{
    private const uint FO_MOVE = 0x1;
    private const uint FO_COPY = 0x2;
    private const uint FO_DELETE = 0x3;

    private const ushort FOF_RENAMEONCOLLISION = 0x0008; // 同名があれば「- コピー」を付ける
    private const ushort FOF_ALLOWUNDO = 0x0040;         // 削除はごみ箱へ / 元に戻せるようにする
    private const ushort FOF_WANTNUKEWARNING = 0x4000;   // ごみ箱に入れられない削除のとき警告する

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEOPSTRUCT
    {
        public IntPtr hwnd;
        public uint wFunc;
        [MarshalAs(UnmanagedType.LPWStr)] public string pFrom;
        [MarshalAs(UnmanagedType.LPWStr)] public string? pTo;
        public ushort fFlags;
        [MarshalAs(UnmanagedType.Bool)] public bool fAnyOperationsAborted;
        public IntPtr hNameMappings;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpszProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHFileOperation(ref SHFILEOPSTRUCT op);

    /// <summary>結果: Done=最後まで実行 / Cancelled=ユーザーが中止 / Failed=エラー。</summary>
    public enum Result { Done, Cancelled, Failed }

    // ───────── コピー / 移動 / 削除 ─────────

    private static bool PathExists(string path) => File.Exists(path) || Directory.Exists(path);

    /// <summary>フォルダ内の項目名の一覧(コピー/移動で、何が増えたかを調べるため)。</summary>
    private static HashSet<string> Snapshot(string dir)
    {
        try
        {
            return new HashSet<string>(
                Directory.EnumerateFileSystemEntries(dir).Select(p => Path.GetFileName(p)),
                StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <summary>sources を destDir へコピー(move=false)または移動(move=true)する。</summary>
    /// <param name="record">元に戻す履歴に記録するか(元に戻す/やり直しの実行中は false)</param>
    /// <param name="createdOut">コピー/移動の結果、destDir に新しくできた項目のパスを受け取る</param>
    public static Result Transfer(IReadOnlyCollection<string> sources, string destDir, bool move, IntPtr owner,
                                  bool record = true, List<string>? createdOut = null)
    {
        if (sources.Count == 0) return Result.Done;

        // 同じフォルダへのコピーは、上書きではなく「- コピー」の名前で作る
        bool sameFolder = sources.All(s => string.Equals(
            Path.GetDirectoryName(s.TrimEnd('\\')), destDir.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase));
        ushort flags = FOF_ALLOWUNDO;
        if (!move && sameFolder) flags |= FOF_RENAMEONCOLLISION;

        var before = Snapshot(destDir);
        var result = Run(move ? FO_MOVE : FO_COPY, sources, destDir, flags, owner);

        // 実際に何ができたかを調べる(途中で中止したときも、できた分だけを元に戻せるようにする)
        var created = Snapshot(destDir).Where(name => !before.Contains(name)).Select(name => Path.Combine(destDir, name)).ToList();
        createdOut?.AddRange(created);

        if (record)
        {
            if (!move)
            {
                if (created.Count > 0) UndoManager.Instance.Push(new CopyAction(sources.ToList(), destDir, created));
            }
            else
            {
                // 同じ名前で移動先に新しく現れ、元から無くなったものだけ記録する。
                // 同名があって上書き/別名になったものは、元に戻すと元の内容を失う恐れがあるので記録しない。
                var pairs = new List<(string From, string To)>();
                foreach (var source in sources)
                {
                    string leaf = Path.GetFileName(source.TrimEnd('\\'));
                    string target = Path.Combine(destDir, leaf);
                    if (!before.Contains(leaf) && PathExists(target) && !PathExists(source)) pairs.Add((source, target));
                }
                if (pairs.Count > 0) UndoManager.Instance.Push(new MoveAction(pairs));
            }
        }
        return result;
    }

    /// <summary>paths を削除する。toRecycleBin=true ならごみ箱へ(元に戻せる)、false なら完全に削除する(元に戻せない)。</summary>
    public static Result Delete(IReadOnlyCollection<string> paths, bool toRecycleBin, IntPtr owner, bool record = true)
    {
        if (paths.Count == 0) return Result.Done;
        ushort flags = toRecycleBin ? (ushort)(FOF_ALLOWUNDO | FOF_WANTNUKEWARNING) : (ushort)0;

        var existed = paths.Where(PathExists).ToList();
        var result = Run(FO_DELETE, paths, null, flags, owner);

        if (record && toRecycleBin)
        {
            var gone = existed.Where(p => !PathExists(p)).ToList();
            if (gone.Count > 0) UndoManager.Instance.Push(new DeleteAction(gone));
        }
        return result;
    }

    private static Result Run(uint func, IEnumerable<string> sources, string? dest, ushort flags, IntPtr owner)
    {
        var op = new SHFILEOPSTRUCT
        {
            hwnd = owner,
            wFunc = func,
            pFrom = string.Join('\0', sources) + "\0\0", // ヌル区切り、二重ヌル終端
            pTo = dest == null ? null : dest + "\0\0",
            fFlags = flags,
        };
        int code = SHFileOperation(ref op);
        if (op.fAnyOperationsAborted) return Result.Cancelled;
        return code == 0 ? Result.Done : Result.Failed;
    }

    // ───────── 新規作成 / 名前の変更 ─────────

    /// <summary>dir の中に、重複しない名前でフォルダを作って、そのパスを返す。</summary>
    public static string CreateFolder(string dir, string baseName, bool record = true)
    {
        string path = UniquePath(dir, baseName, "");
        Directory.CreateDirectory(path);
        if (record) UndoManager.Instance.Push(new CreateAction(path, isDirectory: true));
        return path;
    }

    /// <summary>dir の中に、重複しない名前で空のファイルを作って、そのパスを返す。</summary>
    public static string CreateFile(string dir, string baseName, string extension, bool record = true)
    {
        string path = UniquePath(dir, baseName, extension);
        using (File.Create(path)) { }
        if (record) UndoManager.Instance.Push(new CreateAction(path, isDirectory: false));
        return path;
    }

    private static string UniquePath(string dir, string baseName, string extension)
    {
        string path = Path.Combine(dir, baseName + extension);
        for (int i = 2; File.Exists(path) || Directory.Exists(path); i++)
            path = Path.Combine(dir, $"{baseName} ({i}){extension}");
        return path;
    }

    /// <summary>名前を変更して、新しいパスを返す。不正な名前や重複は例外にする。</summary>
    public static string Rename(string path, string newName, bool record = true)
    {
        newName = newName.Trim();
        if (newName.Length == 0) throw new InvalidOperationException("名前を入力してください。");
        if (newName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new InvalidOperationException("次の文字は使えません: \\ / : * ? \" < > |");
        if (newName.TrimEnd('.', ' ').Length == 0) throw new InvalidOperationException("この名前は使えません。");

        string dir = Path.GetDirectoryName(path.TrimEnd('\\'))!;
        string target = Path.Combine(dir, newName);
        bool isDir = Directory.Exists(path);

        if (string.Equals(path, target, StringComparison.Ordinal)) return path; // 変更なし

        // 大文字小文字だけの変更は、別の名前を経由する
        if (string.Equals(path, target, StringComparison.OrdinalIgnoreCase))
        {
            string temp = Path.Combine(dir, Guid.NewGuid().ToString("N"));
            Move(path, temp, isDir);
            Move(temp, target, isDir);
            if (record) UndoManager.Instance.Push(new RenameAction(path, target));
            return target;
        }

        if (File.Exists(target) || Directory.Exists(target))
            throw new IOException($"「{newName}」はすでに存在します。");
        Move(path, target, isDir);
        if (record) UndoManager.Instance.Push(new RenameAction(path, target));
        return target;
    }

    private static void Move(string from, string to, bool isDir)
    {
        if (isDir) Directory.Move(from, to); else File.Move(from, to);
    }

    // ───────── 判定 ─────────

    /// <summary>destDir が source フォルダ自身、またはその中か(自分自身の中へのコピー/移動の防止)。</summary>
    public static bool IsInsideOrSame(string source, string destDir)
    {
        string s = source.TrimEnd('\\');
        string d = destDir.TrimEnd('\\');
        return string.Equals(s, d, StringComparison.OrdinalIgnoreCase) ||
               d.StartsWith(s + "\\", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>同じドライブか。ドラッグ&ドロップの既定(同じ=移動、違う=コピー)に使う。</summary>
    public static bool IsSameVolume(string a, string b) =>
        string.Equals(Path.GetPathRoot(a), Path.GetPathRoot(b), StringComparison.OrdinalIgnoreCase);
}
