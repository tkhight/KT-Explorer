namespace KT_Explorer.Services;

/// <summary>
/// ファイル操作の「元に戻す / やり直し」の履歴。アプリ全体で1つ(どのタブ・ペインで行った操作も、実行した順に戻せる)。
/// 履歴はメモリ上だけで、アプリを終了すると消える。
/// </summary>
public sealed class UndoManager
{
    public static UndoManager Instance { get; } = new();

    private const int Limit = 50;
    private readonly List<UndoAction> _undo = new();
    private readonly List<UndoAction> _redo = new();

    /// <summary>履歴が変わった(ボタンの有効/無効やツールチップの更新用)。</summary>
    public event Action? Changed;

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public string? UndoText => _undo.Count > 0 ? _undo[^1].Description : null;
    public string? RedoText => _redo.Count > 0 ? _redo[^1].Description : null;

    /// <summary>実行した操作を記録する。新しい操作をすると、やり直しの履歴は消える。</summary>
    public void Push(UndoAction action)
    {
        _undo.Add(action);
        if (_undo.Count > Limit) _undo.RemoveAt(0);
        _redo.Clear();
        Changed?.Invoke();
    }

    /// <summary>直前の操作を元に戻す。成功すれば null、できなければ理由のメッセージ。</summary>
    public string? Undo(IntPtr owner)
    {
        if (_undo.Count == 0) return null;
        var action = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);

        string? error = Execute(() => action.Undo(owner));
        if (error == null) _redo.Add(action);
        // 失敗した操作は履歴から外す(状態が変わっていて戻せないものが、それ以前の履歴を塞がないように)
        Changed?.Invoke();
        return error;
    }

    /// <summary>元に戻した操作をやり直す。成功すれば null、できなければ理由のメッセージ。</summary>
    public string? Redo(IntPtr owner)
    {
        if (_redo.Count == 0) return null;
        var action = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);

        string? error = Execute(() => action.Redo(owner));
        if (error == null) _undo.Add(action);
        Changed?.Invoke();
        return error;
    }

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
        Changed?.Invoke();
    }

    private static string? Execute(Func<string?> run)
    {
        try { return run(); }
        catch (Exception ex) { return ex.Message; }
    }
}
