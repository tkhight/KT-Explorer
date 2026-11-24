using System.Windows.Media;

namespace KT_Explorer.Services;

/// <summary>
/// 1つの項目(一覧の行・ツリーのノード・タブ)のオーバーレイアイコンを、最初に参照されたとき、バックグラウンドで調べて持つ。
/// 調べ終わったら changed を呼ぶ(画面の更新用)。調べ直し(<see cref="Refresh"/>)の間は、いまの表示を残す(ちらつかせない)。
/// </summary>
public sealed class OverlayLoader
{
    private readonly Func<string> _path;
    private readonly Action _changed;
    private OverlayIcons? _overlay;
    private bool _requested;
    private int _version;

    /// <param name="path">調べる対象のパス(調べるたびに取得する。タブのように、パスが変わるものに対応)。</param>
    /// <param name="changed">表示するオーバーレイが変わった。</param>
    public OverlayLoader(Func<string> path, Action changed)
    {
        _path = path;
        _changed = changed;
    }

    /// <summary>小さなオーバーレイ(16px 用)。無い・調べている間・オフのときは null。</summary>
    public ImageSource? Small
    {
        get { Ensure(); return _overlay?.Small; }
    }

    /// <summary>アイコン表示で、大きなアイコンに重ねるオーバーレイ。</summary>
    public ImageSource? Large
    {
        get { Ensure(); return _overlay?.Large; }
    }

    private void Ensure()
    {
        if (_requested || !OverlayIconService.Enabled) return;
        _requested = true;
        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        int version = _version;
        var overlay = await OverlayIconService.GetAsync(_path());
        if (version != _version) return; // 調べている間に、リセットされた(パスが変わった・オフにした)ので、古い結果は捨てる
        if (ReferenceEquals(overlay, _overlay)) return;
        _overlay = overlay;
        _changed();
    }

    /// <summary>状態が変わったかもしれないので、調べ直す。まだ一度も表示していないものは、何もしない。</summary>
    public void Refresh()
    {
        if (!_requested || !OverlayIconService.Enabled) return;
        _ = LoadAsync();
    }

    /// <summary>いったん消して、次に表示するときに調べ直す(パスが変わったとき、「オーバーレイを表示」を切り替えたとき)。</summary>
    public void Reset()
    {
        _version++;
        _overlay = null;
        _requested = false;
        _changed();
    }
}
