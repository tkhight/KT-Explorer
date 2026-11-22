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

    /// <summary>フォルダ風の簡単なアイコンを描いて作る(アイコンファイルを持たないため)。</summary>
    private static Drawing.Icon CreateIcon()
    {
        using var bmp = new Drawing.Bitmap(32, 32);
        using (var g = Drawing.Graphics.FromImage(bmp))
        {
            g.SmoothingMode = Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(Drawing.Color.Transparent);
            using var tab = new Drawing.SolidBrush(Drawing.Color.FromArgb(230, 170, 40));
            using var body = new Drawing.SolidBrush(Drawing.Color.FromArgb(250, 200, 70));
            g.FillRectangle(tab, 2, 6, 13, 7);
            g.FillRectangle(body, 2, 10, 28, 19);
            using var font = new Drawing.Font("Segoe UI", 10, Drawing.FontStyle.Bold, Drawing.GraphicsUnit.Pixel);
            g.DrawString("KT", font, Drawing.Brushes.SaddleBrown, 7, 13);
        }
        IntPtr hIcon = bmp.GetHicon();
        try
        {
            using var temp = Drawing.Icon.FromHandle(hIcon);
            return (Drawing.Icon)temp.Clone(); // ハンドルを手放しても使えるよう複製する
        }
        finally
        {
            DestroyIcon(hIcon);
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);

    public void Dispose()
    {
        _icon.Visible = false; // 終了後にアイコンが残らないよう先に消す
        _icon.Dispose();
        _iconImage.Dispose();
    }
}
