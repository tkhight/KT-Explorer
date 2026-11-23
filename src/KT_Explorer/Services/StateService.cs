using System.IO;
using System.Text.Json;

namespace KT_Explorer.Services;

/// <summary>永続化する状態(タブ一覧・ウィンドウ位置・オプション)。</summary>
/// <summary>タブごとの並び順(TabPaths と同じ順)。</summary>
public sealed class TabSortState
{
    public SortColumn Column { get; set; } = SortColumn.Name;
    public bool Descending { get; set; }

    /// <summary>タブごとの表示(詳細・アイコン・一覧)。古い状態ファイルには無い(その場合は詳細)。</summary>
    public KT_Explorer.Models.ViewMode ViewMode { get; set; } = KT_Explorer.Models.ViewMode.Details;
}

public sealed class GroupState
{
    public string Name { get; set; } = "";
    public bool IsDefault { get; set; }
    public List<string> TabPaths { get; set; } = new();

    /// <summary>TabPaths と同じ順の、タブごとの並び順。古い状態ファイルには無い(その場合は初期の並び順)。</summary>
    public List<TabSortState> TabSorts { get; set; } = new();
    public int SelectedIndex { get; set; }
}

public sealed class FavoriteState
{
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";

    /// <summary>フォルダ / グループ / 区切り線。古い状態ファイルには無い(その場合はフォルダ)。</summary>
    public KT_Explorer.Models.FavoriteKind Kind { get; set; } = KT_Explorer.Models.FavoriteKind.Folder;

    /// <summary>グループの中身。</summary>
    public List<FavoriteState> Children { get; set; } = new();
}

public sealed class AppState
{
    public List<FavoriteState> Favorites { get; set; } = new();

    /// <summary>右ボタンドラッグのマウスジェスチャ(左=戻る / 右=進む / 上=一つ上)。</summary>
    public bool EnableGestures { get; set; } = true;

    /// <summary>外部から開いた Explorer を KT_Explorer のデフォルトグループへ取り込む。</summary>
    public bool CaptureExplorer { get; set; } = true;

    /// <summary>閉じるボタンで終了せず、タスクトレイへ常駐する。</summary>
    public bool CloseToTray { get; set; } = true;

    /// <summary>一覧の並び順(列見出しのクリックで変わる)。全タブ共通。</summary>
    public SortColumn SortColumn { get; set; } = SortColumn.Name;
    public bool SortDescending { get; set; }

    /// <summary>フォルダを、並び順にかかわらず先頭にまとめる。</summary>
    public bool FoldersFirst { get; set; } = true;

    /// <summary>一覧を開いたとき、フォルダの合計サイズを自動で計算する(重いので既定はオフ)。</summary>
    public bool AutoFolderSize { get; set; }

    /// <summary>新しく開くタブの初期の表示(最後に切り替えた表示)。</summary>
    public KT_Explorer.Models.ViewMode ViewMode { get; set; } = KT_Explorer.Models.ViewMode.Details;

    /// <summary>プレビューウィンドウを表示する。</summary>
    public bool ShowPreview { get; set; }

    /// <summary>プレビューウィンドウの幅。</summary>
    public double PreviewWidth { get; set; } = 320;

    /// <summary>タブグループ(先頭がデフォルトグループ)。</summary>
    public List<GroupState> Groups { get; set; } = new();
    public int SelectedGroupIndex { get; set; }

    // Phase 1 の旧形式。読み込み時の移行用にだけ残す。
    public List<string> TabPaths { get; set; } = new();
    public int SelectedIndex { get; set; }
    public bool SyncTreeWithTab { get; set; } = true;
    public double Left { get; set; } = double.NaN;
    public double Top { get; set; } = double.NaN;
    public double Width { get; set; } = 1100;
    public double Height { get; set; } = 700;
    public bool Maximized { get; set; }
}

public static class StateService
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static string StatePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "KT_Explorer", "state.json");

    public static AppState Load()
    {
        try
        {
            if (File.Exists(StatePath))
                return JsonSerializer.Deserialize<AppState>(File.ReadAllText(StatePath)) ?? new AppState();
        }
        catch
        {
            // 壊れた状態ファイルは無視して初期状態で起動する
        }
        return new AppState();
    }

    public static void Save(AppState state)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StatePath)!);
            File.WriteAllText(StatePath, JsonSerializer.Serialize(state, Options));
        }
        catch
        {
            // 保存失敗でアプリを落とさない
        }
    }
}
