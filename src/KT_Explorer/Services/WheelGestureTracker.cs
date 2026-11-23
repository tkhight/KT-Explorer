namespace KT_Explorer.Services;

/// <summary>ホイールのジェスチャで起こす操作。</summary>
public enum WheelAction
{
    None,
    PreviousTab,
    NextTab,
    PreviousGroup,
    NextGroup,
}

/// <summary>
/// 「右ボタンを押したままホイールを回す」ジェスチャの状態。
/// 右ボタンを押している間に、ホイールを上へ回すと左のタブ、下へ回すと右のタブへ切り替える(Ctrl を押していれば、前・次のタブグループへ)。
/// 使ったあとに右ボタンを離したときは、右クリックのメニューを出さない(<see cref="RightUp"/> が true を返す)。
/// </summary>
public sealed class WheelGestureTracker
{
    /// <summary>右ボタンを押している(ジェスチャが有効なとき)。</summary>
    public bool IsHeld { get; private set; }

    /// <summary>右ボタンを押している間に、ホイールのジェスチャを使った。</summary>
    public bool Used { get; private set; }

    /// <summary>右ボタンを押した。ジェスチャが無効(設定でオフ)なら、何も追跡しない。</summary>
    public void RightDown(bool gesturesEnabled)
    {
        IsHeld = gesturesEnabled;
        Used = false;
    }

    /// <summary>
    /// ホイールを回した(上=正、下=負)。右ボタンを押している間だけ、左のタブ・右のタブを返す。
    /// ctrl が true(Ctrl を押している)なら、前・次のタブグループを返す。
    /// </summary>
    public WheelAction Wheel(int delta, bool ctrl = false)
    {
        if (!IsHeld || delta == 0) return WheelAction.None;
        Used = true;
        if (ctrl) return delta > 0 ? WheelAction.PreviousGroup : WheelAction.NextGroup;
        return delta > 0 ? WheelAction.PreviousTab : WheelAction.NextTab;
    }

    /// <summary>右ボタンを離した。戻り値: ホイールのジェスチャを使ったので、右クリックのメニューを出さない。</summary>
    public bool RightUp()
    {
        bool suppress = Used;
        Cancel();
        return suppress;
    }

    /// <summary>追跡をやめる(右ボタンの離しを見逃したかもしれないとき。ウィンドウが非アクティブになった、左ボタンが押された)。</summary>
    public void Cancel()
    {
        IsHeld = false;
        Used = false;
    }
}
