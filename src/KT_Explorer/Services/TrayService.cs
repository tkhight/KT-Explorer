using Forms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace KT_Explorer.Services;

/// <summary>タスクトレイのアイコンとメニュー。</summary>
public sealed class TrayService : IDisposable
{
    private readonly Forms.NotifyIcon _icon;
    private readonly Drawing.Icon _iconImage;
    private bool _hintShown;

    /// <param name="show">ウィンドウを表示する</param>
    /// <param name="exit">アプリを終了する</param>
    /// <param name="getCapture">Explorer 取り込みの現在値</param>
    /// <param name="setCapture">Explorer 取り込みの設定</param>
    public TrayService(Action show, Action exit, Func<bool> getCapture, Action<bool> setCapture)
    {
        _iconImage = CreateIcon();

        var open = new Forms.ToolStripMenuItem("KT_Explorer を表示") { Font = new Drawing.Font(Forms.Control.DefaultFont, Drawing.FontStyle.Bold) };
        open.Click += (_, _) => show();

        var capture = new Forms.ToolStripMenuItem("開いた Explorer を取り込む") { CheckOnClick = true };
        capture.Click += (_, _) => setCapture(capture.Checked);

        var quit = new Forms.ToolStripMenuItem("終了");
        quit.Click += (_, _) => exit();

        var menu = new Forms.ContextMenuStrip();
        menu.Items.AddRange(new Forms.ToolStripItem[] { open, new Forms.ToolStripSeparator(), capture, new Forms.ToolStripSeparator(), quit });
        menu.Opening += (_, _) => capture.Checked = getCapture(); // 開くたびに現在の設定へ合わせる

        _icon = new Forms.NotifyIcon
        {
            Icon = _iconImage,
            Text = "KT_Explorer",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.MouseClick += (_, e) => { if (e.Button == Forms.MouseButtons.Left) show(); };
    }

    /// <summary>初めてトレイへ隠れたときだけ、常駐していることを知らせる。</summary>
    public void ShowHintOnce()
    {
        if (_hintShown) return;
        _hintShown = true;
        _icon.ShowBalloonTip(3000, "KT_Explorer", "トレイに常駐しています。アイコンをクリックすると表示します。", Forms.ToolTipIcon.Info);
    }

    /// <summary>アプリのアイコン(リソースの app.ico)を、トレイの大きさで読む。読めなければ、既定のアプリのアイコン。</summary>
    private static Drawing.Icon CreateIcon()
    {
        try
        {
            var info = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/KT_Explorer;component/Resources/app.ico"));
            if (info != null)
            {
                using var stream = info.Stream;
                return new Drawing.Icon(stream, Forms.SystemInformation.SmallIconSize); // トレイの大きさに合う画像を選ぶ
            }
        }
        catch (Exception) { /* リソースが読めなければ、下の既定のアイコン */ }
        return Drawing.SystemIcons.Application;
    }

    public void Dispose()
    {
        _icon.Visible = false; // 終了後にアイコンが残らないよう先に消す
        _icon.Dispose();
        _iconImage.Dispose();
    }
}
