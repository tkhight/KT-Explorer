using System.Collections;
using KT_Explorer.Models;

namespace KT_Explorer.Services;

public enum SortColumn { Name, Modified, Type, Size }

/// <summary>
/// 並べ替えの、アプリ全体の設定。並び順そのものは、タブ(ペイン)ごとに持つ(FolderTabViewModel)。
/// ここが持つのは、新しく開くタブの初期の並び順(最後に列見出しをクリックした並び順)と、
/// 「フォルダーを先頭にまとめる」(全タブ共通)。状態ファイルに保存する。
/// </summary>
public sealed class SortSettings
{
    public static SortSettings Instance { get; } = new();

    /// <summary>新しく開くタブの初期の並び順(最後に列見出しをクリックした並び順)。</summary>
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

    /// <summary>列見出しをクリックした並び順を、新しいタブの初期の並び順として覚える。</summary>
    public void SetDefault(SortColumn column, bool descending)
    {
        Column = column;
        Descending = descending;
    }

    public void SetFoldersFirst(bool value)
    {
        if (FoldersFirst == value) return;
        FoldersFirst = value;
        Changed?.Invoke();
    }

    /// <summary>指定した並び順(と、フォルダー先頭の設定)で並べる比較用(ICollectionView の CustomSort に渡す)。</summary>
    public IComparer CreateComparer(SortColumn column, bool descending) => new EntryComparer(column, descending, FoldersFirst);

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
