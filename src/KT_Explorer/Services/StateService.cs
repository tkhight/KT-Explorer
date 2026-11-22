using System.IO;
using System.Text.Json;

namespace KT_Explorer.Services;

/// <summary>永続化する状態(タブ一覧・ウィンドウ位置・オプション)。</summary>
public sealed class AppState
{
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
