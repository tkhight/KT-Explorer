namespace KT_Explorer.Models;

/// <summary>一覧の表示のしかた。</summary>
public enum ViewMode
{
    /// <summary>詳細: 名前・更新日時・種類・サイズの列。</summary>
    Details,

    /// <summary>アイコン: 大きなアイコン(画像・動画などはサムネイル)を、名前つきで並べる。</summary>
    Icons,

    /// <summary>一覧: 小さなアイコンと名前を、上から下へ、列に折り返して並べる。</summary>
    List,
}
