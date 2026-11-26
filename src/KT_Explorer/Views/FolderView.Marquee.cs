using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;

namespace KT_Explorer.Views;

/// <summary>
/// 一覧の範囲選択(ドラッグで枠を描いて、重なった項目を選ぶ)。
/// 項目のアイコンと名前からのドラッグは、これまでどおりファイルの移動・コピー。それ以外(左右の余白・下の空き・
/// 更新日時などの列)からのドラッグが、範囲選択になる。Ctrl を押しながらなら、今の選択に追加する。
/// </summary>
public partial class FolderView
{
    private bool _marqueeArmed;                 // ボタンを押した(まだ、枠を描くほど動いていない)
    private bool _marqueeActive;                // 枠を描いている
    private bool _marqueeAdd;                   // Ctrl: 今の選択に追加する
    private bool _marqueeOnItem;                // 項目の行の上で始めた(クリックだけなら、WPF の通常の選択に任せる)
    private Point _marqueeStart;                // 押した位置(表示領域の座標)
    private Point _marqueeAnchor;               // 押した位置(中身の座標。スクロールしても動かない)
    private List<object> _marqueeBase = new();  // 押す前の選択
    private ScrollViewer? _marqueeScroll;
    private FrameworkElement? _marqueeViewport; // 項目が表示される領域(列見出し・スクロールバーを除く)
    private DispatcherTimer? _marqueeTimer;

    /// <summary>左ボタンを押した位置が、範囲選択を始める場所なら、準備して true。</summary>
    private bool ArmMarquee(MouseButtonEventArgs e, ListViewItem? item)
    {
        _marqueeArmed = false;
        if (e.ClickCount != 1 || (Keyboard.Modifiers & ModifierKeys.Shift) != 0) return false; // ダブルクリックと Shift の範囲選択は、通常の動き
        if (e.OriginalSource is not DependencyObject source) return false;
        if (FindAncestor<ScrollBar>(source) != null || FindAncestor<GridViewColumnHeader>(source) != null) return false;
        if (item != null && IsOnNameOrIcon(source)) return false; // ここからのドラッグは、ファイルの移動・コピー

        _marqueeScroll = FindDescendant<ScrollViewer>(FileList);
        _marqueeViewport = _marqueeScroll?.Template.FindName("PART_ScrollContentPresenter", _marqueeScroll) as FrameworkElement;
        if (_marqueeScroll == null || _marqueeViewport == null) return false;

        _marqueeArmed = true;
        _marqueeOnItem = item != null;
        _marqueeAdd = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        _marqueeBase = FileList.SelectedItems.Cast<object>().ToList();
        _marqueeStart = e.GetPosition(_marqueeViewport);
        var offset = ScrollOffsetPixels();
        _marqueeAnchor = new Point(_marqueeStart.X + offset.X, _marqueeStart.Y + offset.Y);

        if (item == null)
        {
            // 空いた場所は、WPF 側に何も処理が無いので、ここでフォーカスとマウスを受け取る
            FileList.Focus();
            ListHost.CaptureMouse();
            e.Handled = true;
        }
        return true;
    }

    private static bool IsOnNameOrIcon(DependencyObject source) =>
        source is Image || (source as FrameworkElement)?.Tag as string == "NameText";

    private void Marquee_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!_marqueeArmed && !_marqueeActive) return;
        if (e.LeftButton != MouseButtonState.Pressed) { EndMarquee(); return; }

        if (!_marqueeActive)
        {
            var delta = e.GetPosition(_marqueeViewport) - _marqueeStart;
            if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance) return;

            _marqueeActive = true;
            _marqueeArmed = false;
            _renameTimer.Stop();
            _renameCandidate = null;
            ListHost.CaptureMouse(); // ListView 側の「ドラッグで選択を広げる」動きは、ここで止まる
            _marqueeTimer ??= new DispatcherTimer(TimeSpan.FromMilliseconds(40), DispatcherPriority.Input, MarqueeAutoScroll, Dispatcher);
            _marqueeTimer.Start();
        }
        UpdateMarquee();
        e.Handled = true;
    }

    private void Marquee_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_marqueeActive) { UpdateMarquee(); e.Handled = true; }
        else if (_marqueeArmed && !_marqueeOnItem && !_marqueeAdd) FileList.UnselectAll(); // 余白をクリックしただけ → 選択を解除
        else if (!_marqueeArmed) return;
        EndMarquee();
    }

    private void Marquee_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!_marqueeActive || e.Key != Key.Escape) return;
        SetSelection(_marqueeBase); // Esc: 押す前の選択に戻す
        EndMarquee();
        e.Handled = true;
    }

    private void Marquee_LostMouseCapture(object sender, MouseEventArgs e)
    {
        // 子(ListView)がマウスを手放したときの通知も届くので、自分が持っていたものを失ったときだけ終える
        if (e.OriginalSource == ListHost && Mouse.Captured != ListHost) EndMarquee();
    }

    private void EndMarquee()
    {
        _marqueeArmed = false;
        _marqueeActive = false;
        _marqueeTimer?.Stop();
        MarqueeRect.Visibility = Visibility.Collapsed;
        _marqueeBase = new();
        if (Mouse.Captured == ListHost) ListHost.ReleaseMouseCapture();
    }

    /// <summary>枠を表示領域の外へ出して止めている間、その方向へスクロールする。</summary>
    private void MarqueeAutoScroll(object? sender, EventArgs e)
    {
        if (!_marqueeActive || _marqueeScroll == null || _marqueeViewport == null) return;
        var inView = Mouse.GetPosition(_marqueeViewport);
        var inList = Mouse.GetPosition(FileList);
        bool up = inView.Y < 0, down = inView.Y > _marqueeViewport.ActualHeight;
        bool left = inList.X < 0, right = inList.X > FileList.ActualWidth;
        if (!(up || down || left || right)) return;

        if (up) _marqueeScroll.LineUp();
        if (down) _marqueeScroll.LineDown();
        if (left) _marqueeScroll.LineLeft();
        if (right) _marqueeScroll.LineRight();
        FileList.UpdateLayout();
        UpdateMarquee();
    }

    /// <summary>
    /// 詳細表示の行は、見えている分だけが作られ、縦のスクロール量は「行数」で数える。そのときの 1 行の高さ。
    /// それ以外(アイコン・一覧)は、スクロール量がピクセルなので 1。
    /// </summary>
    private double VerticalScrollUnit() =>
        FileList.View is GridView && FirstRealizedRow() is { } row ? RowPitch(row.Item) : 1;

    /// <summary>行の送り幅。行の間の余白も含める(ActualHeight だけだと、下の行ほど、位置がずれる)。</summary>
    private static double RowPitch(ListViewItem item) => LayoutInformation.GetLayoutSlot(item).Height;

    private Point ScrollOffsetPixels() =>
        new(_marqueeScroll!.HorizontalOffset, _marqueeScroll.VerticalOffset * VerticalScrollUnit());

    private (int Index, ListViewItem Item)? FirstRealizedRow()
    {
        int count = FileList.Items.Count;
        int from = Math.Clamp((int)(_marqueeScroll?.VerticalOffset ?? 0), 0, Math.Max(0, count - 1));
        for (int i = from; i < count; i++)
            if (FileList.ItemContainerGenerator.ContainerFromIndex(i) is ListViewItem { ActualHeight: > 0 } item) return (i, item);
        return null;
    }

    private void UpdateMarquee()
    {
        if (_marqueeViewport == null || _marqueeScroll == null) return;
        var offset = ScrollOffsetPixels();
        var anchor = new Point(_marqueeAnchor.X - offset.X, _marqueeAnchor.Y - offset.Y);
        var area = new Rect(anchor, Mouse.GetPosition(_marqueeViewport));

        var hits = ItemsIn(area);
        if (_marqueeAdd) hits.UnionWith(_marqueeBase);
        SetSelection(hits);
        DrawMarquee(area);
    }

    /// <summary>枠(表示領域の座標)に重なる項目。詳細表示は、まだ作られていない行も、行の高さから計算して含める。</summary>
    private HashSet<object> ItemsIn(Rect area)
    {
        var hits = new HashSet<object>();
        if (FileList.View is GridView)
        {
            if (FirstRealizedRow() is not { } row) return hits;
            var origin = row.Item.TranslatePoint(new Point(), _marqueeViewport);
            double height = RowPitch(row.Item);
            origin.Y -= row.Item.Margin.Top;
            if (area.Right < origin.X || area.Left > origin.X + row.Item.ActualWidth) return hits;
            int first = Math.Max(0, row.Index + (int)Math.Floor((area.Top - origin.Y) / height));
            int last = Math.Min(FileList.Items.Count - 1, row.Index + (int)Math.Floor((area.Bottom - origin.Y) / height));
            for (int i = first; i <= last; i++) hits.Add(FileList.Items[i]);
            return hits;
        }

        for (int i = 0; i < FileList.Items.Count; i++)
        {
            if (FileList.ItemContainerGenerator.ContainerFromIndex(i) is not ListViewItem item) continue;
            if (area.IntersectsWith(new Rect(item.TranslatePoint(new Point(), _marqueeViewport), item.RenderSize))) hits.Add(FileList.Items[i]);
        }
        return hits;
    }

    /// <summary>選択を、指定の項目にする(変わる分だけを、足し引きする)。</summary>
    private void SetSelection(IEnumerable<object> items)
    {
        var want = new HashSet<object>(items);
        foreach (var old in FileList.SelectedItems.Cast<object>().Where(x => !want.Contains(x)).ToList())
            FileList.SelectedItems.Remove(old);
        want.ExceptWith(FileList.SelectedItems.Cast<object>());
        if (want.Count == 0) return;
        foreach (var item in FileList.Items.Cast<object>().Where(want.Contains).ToList())
            FileList.SelectedItems.Add(item);
    }

    private void DrawMarquee(Rect area)
    {
        var viewTop = _marqueeViewport!.TranslatePoint(new Point(), ListHost).Y;
        var bounds = new Rect(0, viewTop, ListHost.ActualWidth, _marqueeViewport.ActualHeight);
        var shown = new Rect(_marqueeViewport.TranslatePoint(area.TopLeft, ListHost), area.Size);
        shown.Intersect(bounds);
        if (shown.IsEmpty) { MarqueeRect.Visibility = Visibility.Collapsed; return; }

        Canvas.SetLeft(MarqueeRect, shown.X);
        Canvas.SetTop(MarqueeRect, shown.Y);
        MarqueeRect.Width = shown.Width;
        MarqueeRect.Height = shown.Height;
        MarqueeRect.Visibility = Visibility.Visible;
    }
}
