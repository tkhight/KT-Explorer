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

    /// <summary>ピン留め。古い状態ファイルには無い(その場合は留めない)。</summary>
    public bool IsPinned { get; set; }

    /// <summary>タブの色(#RRGGBB)。古い状態ファイルには無い(その場合は色なし)。</summary>
    public string? Color { get; set; }
}

public sealed class GroupState
{
    public string Name { get; set; } = "";
    public bool IsDefault { get; set; }

    /// <summary>グループの色(#RRGGBB)。古い状態ファイルには無い(その場合は色なし)。</summary>
    public string? Color { get; set; }
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

/// <summary>開いたフォルダの履歴の1件(状態ファイル用)。</summary>
public sealed class VisitState
{
    public string Path { get; set; } = "";
    public int Count { get; set; }
    public DateTime LastVisited { get; set; }
}

public sealed class AppState
{
    /// <summary>開いたフォルダの履歴(最近開いたフォルダ・よく開くフォルダ・パス欄の補完に使う)。</summary>
    public List<VisitState> Visits { get; set; } = new();

    public List<FavoriteState> Favorites { get; set; } = new();

    /// <summary>右ボタンドラッグのマウスジェスチャ(左=戻る / 右=進む / 上=一つ上)。</summary>
    public bool EnableGestures { get; set; } = true;

    /// <summary>外部から開いた Explorer を KT_Explorer のデフォルトグループへ取り込む。
    /// 初めて使う人が、エクスプローラーが勝手に閉じたと驚かないように、既定はオフ。</summary>
    public bool CaptureExplorer { get; set; }

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

    /// <summary>詳細表示で追加している列(created / attributes / dimensions / duration)。</summary>
    public List<string> ExtraColumns { get; set; } = new();

    /// <summary>アイコン表示の大きさ(小・中・大)。</summary>
    public IconSize IconSize { get; set; } = IconSize.Medium;

    /// <summary>プレビューウィンドウを表示する。</summary>
    public bool ShowPreview { get; set; }

    /// <summary>フォルダタブを、1 段で横にスクロールさせず、幅に収まらない分を次の段へ折り返して、すべて見えるように表示する。</summary>
    public bool MultiRowTabs { get; set; }

    /// <summary>同じ名前のフォルダタブに、親フォルダの名前を添える。</summary>
    public bool ShowParentNames { get; set; } = true;

    /// <summary>一覧のアイコンに、シェルのオーバーレイ(OneDrive・SVN などの状態)を重ねる。</summary>
    public bool ShowOverlayIcons { get; set; } = true;

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

    /// <summary>状態の保存先。環境変数 KT_EXPLORER_STATE_DIR があれば、そのフォルダ(説明用の画面の撮影などで、普段の設定を使わないため)。</summary>
    public static string StatePath { get; } = Path.Combine(
        Environment.GetEnvironmentVariable("KT_EXPLORER_STATE_DIR") is { Length: > 0 } dir
            ? dir
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "KT_Explorer"),
        "state.json");

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
