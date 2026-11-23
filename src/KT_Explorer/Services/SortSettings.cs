using System.Collections;
using KT_Explorer.Models;

namespace KT_Explorer.Services;

public enum SortColumn { Name, Modified, Type, Size }

/// <summary>
/// 一覧の並び順(列見出しのクリックで切り替える)。アプリ全体で1つで、すべてのタブ・ペインに適用する。
/// 状態ファイルに保存する。
/// </summary>
public sealed class SortSettings
{
    public static SortSettings Instance { get; } = new();

    public SortColumn Column { get; private set; } = SortColumn.Name;
    public bool Descending { get; private set; }

    /// <summary>フォルダを、並び順にかかわらず先頭にまとめる。</summary>
    public bool FoldersFirst { get; private set; } = true;

    public event Action? Changed;

    public void Load(SortColumn column, bool descending, bool foldersFirst)
    {
        Column = column;
        Descending = descending;
        FoldersFirst = foldersFirst;
    }

    /// <summary>列見出しのクリック。別の列なら昇順で、同じ列なら昇順/降順を入れ替える。</summary>
    public void Toggle(SortColumn column)
    {
        if (column == Column) Descending = !Descending;
        else { Column = column; Descending = false; }
        Changed?.Invoke();
    }

    public void SetFoldersFirst(bool value)
    {
        if (FoldersFirst == value) return;
        FoldersFirst = value;
        Changed?.Invoke();
    }

    /// <summary>現在の設定で並べる比較用(ICollectionView の CustomSort に渡す)。</summary>
    public IComparer CreateComparer() => new EntryComparer(Column, Descending, FoldersFirst);

    private sealed class EntryComparer(SortColumn column, bool descending, bool foldersFirst) : IComparer
    {
        public int Compare(object? x, object? y)
        {
            if (x is not FileEntry a || y is not FileEntry b) return 0;

            if (foldersFirst && a.IsDirectory != b.IsDirectory) return a.IsDirectory ? -1 : 1;

            int result = column switch
            {
                SortColumn.Modified => a.Modified.CompareTo(b.Modified),
                SortColumn.Type => string.Compare(a.TypeText, b.TypeText, StringComparison.CurrentCultureIgnoreCase),
                SortColumn.Size => (a.Size ?? -1).CompareTo(b.Size ?? -1), // フォルダ(サイズなし)は最小
                _ => ByName(a, b),
            };
            if (descending) result = -result;

            // 同じ値のときは、名前の昇順で安定させる
            return result != 0 ? result : ByName(a, b);
        }

        private static int ByName(FileEntry a, FileEntry b) =>
            string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase);
    }
}
