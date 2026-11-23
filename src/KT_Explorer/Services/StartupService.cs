using Microsoft.Win32;

namespace KT_Explorer.Services;

/// <summary>
/// Windows へのサインイン時に自動で起動する設定。現在のユーザーの「スタートアップ」レジストリ
/// (HKCU\Software\Microsoft\Windows\CurrentVersion\Run)に登録する。管理者権限は不要。
/// 状態は設定ファイルではなくレジストリを正とするので、タスクマネージャーなど外部で切り替えても食い違わない。
/// </summary>
public static class StartupService
{
    /// <summary>自動起動のときに付ける引数。ウィンドウを出さず、トレイだけに常駐して始める。</summary>
    public const string StartupArg = "--startup";

    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "KT_Explorer";

    public static bool IsEnabled
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKey);
                return key?.GetValue(ValueName) is string;
            }
            catch { return false; }
        }
    }

    /// <summary>登録/解除する。成功すれば true。</summary>
    public static bool SetEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (enabled)
            {
                string? exe = Environment.ProcessPath;
                if (string.IsNullOrEmpty(exe)) return false;
                key.SetValue(ValueName, $"\"{exe}\" {StartupArg}");
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }
            return true;
        }
        catch { return false; }
    }
}
