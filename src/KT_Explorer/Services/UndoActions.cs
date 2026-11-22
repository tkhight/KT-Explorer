using System.IO;

namespace KT_Explorer.Services;

/// <summary>元に戻せる1回分の操作。Undo/Redo は、成功すれば null、できなければ理由のメッセージを返す。</summary>
public abstract class UndoAction
{
    public abstract string Description { get; }
    public abstract string? Undo(IntPtr owner);
    public abstract string? Redo(IntPtr owner);

    protected static bool Exists(string path) => File.Exists(path) || Directory.Exists(path);

    protected static string Names(IEnumerable<string> paths)
    {
        var names = paths.Select(p => Path.GetFileName(p.TrimEnd('\\'))).ToList();
        return names.Count <= 3 ? string.Join("、", names) : string.Join("、", names.Take(3)) + $" ほか {names.Count - 3} 件";
    }

    protected static string? FromResult(FileOperationService.Result result) => result switch
    {
        FileOperationService.Result.Done => null,
        FileOperationService.Result.Cancelled => "操作が中止されました。",
        _ => "一部の項目を処理できませんでした。",
    };
}

/// <summary>コピー。元に戻す=作られた項目をごみ箱へ / やり直し=もう一度コピー。</summary>
internal sealed class CopyAction : UndoAction
{
    private readonly List<string> _sources;
    private readonly string _destDir;
    private List<string> _created;

    public CopyAction(List<string> sources, string destDir, List<string> created)
    {
        _sources = sources;
        _destDir = destDir;
        _created = created;
    }

    public override string Description => $"コピー({_created.Count} 件)";

    public override string? Undo(IntPtr owner)
    {
        var existing = _created.Where(Exists).ToList();
        if (existing.Count == 0) return $"コピーした項目が見つかりません: {Names(_created)}";
        return FromResult(FileOperationService.Delete(existing, toRecycleBin: true, owner, record: false));
    }

    public override string? Redo(IntPtr owner)
    {
        var existing = _sources.Where(Exists).ToList();
        if (existing.Count == 0) return $"コピー元が見つかりません: {Names(_sources)}";
        var created = new List<string>();
        var result = FileOperationService.Transfer(existing, _destDir, move: false, owner, record: false, createdOut: created);
        if (created.Count > 0) _created = created; // 名前が変わることがあるので、作られた項目を取り直す
        return FromResult(result);
    }
}

/// <summary>移動。元に戻す=元の場所へ戻す / やり直し=もう一度移動。</summary>
internal sealed class MoveAction : UndoAction
{
    private readonly List<(string From, string To)> _pairs;

    public MoveAction(List<(string From, string To)> pairs) => _pairs = pairs;

    public override string Description => $"移動({_pairs.Count} 件)";

    public override string? Undo(IntPtr owner)
    {
        var present = _pairs.Where(p => Exists(p.To)).ToList();
        if (present.Count == 0) return $"移動した項目が見つかりません: {Names(_pairs.Select(p => p.To))}";

        var blocked = present.Where(p => Exists(p.From)).ToList();
        if (blocked.Count > 0) return $"元の場所に同じ名前の項目があるため、戻せません: {Names(blocked.Select(p => p.From))}";

        return Move(present.Select(p => (p.To, Path.GetDirectoryName(p.From.TrimEnd('\\'))!)), owner);
    }

    public override string? Redo(IntPtr owner)
    {
        var present = _pairs.Where(p => Exists(p.From)).ToList();
        if (present.Count == 0) return $"移動元が見つかりません: {Names(_pairs.Select(p => p.From))}";

        var blocked = present.Where(p => Exists(p.To)).ToList();
        if (blocked.Count > 0) return $"移動先に同じ名前の項目があるため、移動できません: {Names(blocked.Select(p => p.To))}";

        return Move(present.Select(p => (p.From, Path.GetDirectoryName(p.To.TrimEnd('\\'))!)), owner);
    }

    /// <summary>項目を、それぞれの移動先フォルダごとにまとめて移動する。</summary>
    private static string? Move(IEnumerable<(string Item, string DestDir)> moves, IntPtr owner)
    {
        foreach (var group in moves.GroupBy(m => m.DestDir, StringComparer.OrdinalIgnoreCase))
        {
            var result = FileOperationService.Transfer(group.Select(m => m.Item).ToList(), group.Key, move: true, owner, record: false);
            string? error = FromResult(result);
            if (error != null) return error;
        }
        return null;
    }
}

/// <summary>ごみ箱への削除。元に戻す=ごみ箱から復元 / やり直し=もう一度ごみ箱へ。</summary>
internal sealed class DeleteAction : UndoAction
{
    private readonly List<string> _paths;

    public DeleteAction(List<string> paths) => _paths = paths;

    public override string Description => $"削除({_paths.Count} 件)";

    public override string? Undo(IntPtr owner)
    {
        // 元の場所に同名の項目があると、復元できない(同じ名前を作り直した場合など)。黙って成功扱いにはしない
        var blocked = _paths.Where(Exists).ToList();
        if (blocked.Count > 0) return $"元の場所に同じ名前の項目があるため、戻せません: {Names(blocked)}";

        var failed = new List<string>();
        foreach (var path in _paths)
            if (!RecycleBinService.Restore(path)) failed.Add(path);
        return failed.Count == 0 ? null : $"ごみ箱から元に戻せませんでした(空にされた可能性があります): {Names(failed)}";
    }

    public override string? Redo(IntPtr owner)
    {
        var existing = _paths.Where(Exists).ToList();
        if (existing.Count == 0) return $"削除する項目が見つかりません: {Names(_paths)}";
        return FromResult(FileOperationService.Delete(existing, toRecycleBin: true, owner, record: false));
    }
}

/// <summary>名前の変更。元に戻す=元の名前へ / やり直し=もう一度変更。</summary>
internal sealed class RenameAction : UndoAction
{
    private readonly string _oldPath;
    private readonly string _newPath;

    public RenameAction(string oldPath, string newPath)
    {
        _oldPath = oldPath;
        _newPath = newPath;
    }

    public override string Description =>
        $"名前の変更({Path.GetFileName(_oldPath.TrimEnd('\\'))} → {Path.GetFileName(_newPath.TrimEnd('\\'))})";

    public override string? Undo(IntPtr owner) => Apply(_newPath, _oldPath);

    public override string? Redo(IntPtr owner) => Apply(_oldPath, _newPath);

    private static string? Apply(string from, string to)
    {
        if (!Exists(from)) return $"「{Path.GetFileName(from.TrimEnd('\\'))}」が見つかりません。";
        bool onlyCase = string.Equals(from, to, StringComparison.OrdinalIgnoreCase);
        if (!onlyCase && Exists(to)) return $"「{Path.GetFileName(to.TrimEnd('\\'))}」がすでに存在するため、名前を戻せません。";

        FileOperationService.Rename(from, Path.GetFileName(to.TrimEnd('\\')), record: false);
        return null;
    }
}

/// <summary>新規作成。元に戻す=ごみ箱へ(中身を入れていても失われない) / やり直し=ごみ箱から戻す(無ければ空で作り直す)。</summary>
internal sealed class CreateAction : UndoAction
{
    private readonly string _path;
    private readonly bool _isDirectory;

    public CreateAction(string path, bool isDirectory)
    {
        _path = path;
        _isDirectory = isDirectory;
    }

    public override string Description => $"新規作成({Path.GetFileName(_path.TrimEnd('\\'))})";

    public override string? Undo(IntPtr owner)
    {
        if (!Exists(_path)) return $"「{Path.GetFileName(_path.TrimEnd('\\'))}」が見つかりません。";
        return FromResult(FileOperationService.Delete(new[] { _path }, toRecycleBin: true, owner, record: false));
    }

    public override string? Redo(IntPtr owner)
    {
        if (Exists(_path)) return $"「{Path.GetFileName(_path.TrimEnd('\\'))}」がすでに存在します。";
        if (RecycleBinService.Restore(_path)) return null;

        // ごみ箱に無ければ、空の項目として作り直す
        if (_isDirectory) Directory.CreateDirectory(_path);
        else using (File.Create(_path)) { }
        return null;
    }
}
