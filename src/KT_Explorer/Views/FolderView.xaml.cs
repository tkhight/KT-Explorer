using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using KT_Explorer.Models;
using KT_Explorer.Services;
using KT_Explorer.ViewModels;

namespace KT_Explorer.Views;

/// <summary>1つのフォルダの一覧表示とファイル操作。通常のタブとペインタブの各ペインで共用する。</summary>
public partial class FolderView : UserControl
{
    [DllImport("user32.dll")] private static extern uint GetDoubleClickTime();

    private readonly DispatcherTimer _renameTimer = new();
    private FileEntry? _renameCandidate;   // ゆっくり2回クリックで名前の変更を始める候補
    private readonly DispatcherTimer _hintTimer = new() { Interval = TimeSpan.FromSeconds(2.5) };
    private System.Windows.Controls.ToolTip? _hint;

    public FolderView()
    {
        InitializeComponent();
        _renameTimer.Tick += RenameTimer_Tick;
        _hintTimer.Tick += (_, _) => CloseHint();
        Loaded += (_, _) => { SortSettings.Instance.Changed += UpdateSortHeaders; UpdateSortHeaders(); };
        Unloaded += (_, _) =>
        {
            SortSettings.Instance.Changed -= UpdateSortHeaders;
            _renameTimer.Stop();
            CloseHint();
        };
    }

    // ───────── 列見出しクリックでの並べ替え ─────────

    private static readonly string[] ColumnNames = { "名前", "更新日時", "種類", "サイズ" };

    private void ColumnHeader_Click(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is not GridViewColumnHeader { Column: { } column } || FileList.View is not GridView grid) return; // 余白部分のヘッダーは対象外
        int index = grid.Columns.IndexOf(column);
        if (index < 0 || index >= ColumnNames.Length) return;
        SortSettings.Instance.Toggle((SortColumn)index);
    }

    /// <summary>並べ替えに使っている列の見出しに ▲(昇順)/ ▼(降順)を付ける。</summary>
    private void UpdateSortHeaders()
    {
        if (FileList.View is not GridView grid) return;
        var sort = SortSettings.Instance;
        for (int i = 0; i < grid.Columns.Count && i < ColumnNames.Length; i++)
            grid.Columns[i].Header = (int)sort.Column == i ? ColumnNames[i] + (sort.Descending ? " ▼" : " ▲") : ColumnNames[i];
    }

    private FolderTabViewModel? Vm => DataContext as FolderTabViewModel;

    private IntPtr Owner
    {
        get
        {
            var w = Window.GetWindow(this) ?? Application.Current.MainWindow;
            return w == null ? IntPtr.Zero : new WindowInteropHelper(w).Handle;
        }
    }

    private List<FileEntry> Selected => FileList.SelectedItems.Cast<FileEntry>().ToList();

    // ペイン内で操作を始めたら、そのペインを操作対象にする
    private void OnActivate(object sender, RoutedEventArgs e) => Vm?.RequestActivate();

    // ───────── 開く / キー操作 ─────────

    private void FileList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (IsInEditBox(e.OriginalSource)) return;
        _renameTimer.Stop();
        _renameCandidate = null;
        if (e.OriginalSource is DependencyObject d && FindAncestor<ListViewItem>(d) != null
            && FileList.SelectedItem is FileEntry entry)
            Vm?.Open(entry);
    }

    private void FileList_KeyDown(object sender, KeyEventArgs e)
    {
        // 名前の編集中のキー(Backspace や Delete など)を、一覧の操作として扱わない
        if (IsInEditBox(e.OriginalSource)) return;
        _renameTimer.Stop();

        bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;

        if (e.Key == Key.Enter && FileList.SelectedItem is FileEntry entry) Vm?.Open(entry);
        else if (e.Key == Key.Back) Vm?.GoUp();
        else if (ctrl && e.Key == Key.C) CopySelection(cut: false);
        else if (ctrl && e.Key == Key.X) CopySelection(cut: true);
        else if (ctrl && e.Key == Key.V) Paste(null);
        else if (ctrl && shift && e.Key == Key.N) NewItem(folder: true);
        else if (ctrl && shift && e.Key == Key.C) TransferToPane(NextPane(), move: false);
        else if (ctrl && shift && e.Key == Key.M) TransferToPane(NextPane(), move: true);
        else if (e.Key == Key.Delete) DeleteSelection(permanent: shift);
        else if (e.Key == Key.F2) RenameSelected();
        else return;
        e.Handled = true;
    }

    // ───────── コピー / 切り取り / 貼り付け ─────────

    private void CopySelection(bool cut)
    {
        var sel = Selected;
        if (sel.Count == 0) return;
        if (!ClipboardService.SetFiles(sel.Select(s => s.FullPath), cut))
            MessageBox.Show("クリップボードを使用できませんでした。", "KT_Explorer", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    /// <summary>クリップボードのファイルを貼り付ける。destDir が null なら表示中のフォルダ。</summary>
    private async void Paste(string? destDir)
    {
        var vm = Vm;
        var clip = ClipboardService.GetFiles();
        if (vm == null || clip == null) return;

        string dest = destDir ?? vm.Path;
        var (paths, cut) = clip.Value;
        if (!CanTransfer(paths, dest)) return;

        if (cut) paths = paths.Where(p => !IsInFolder(p, dest)).ToList(); // 切り取り→同じ場所へは何もしない
        if (paths.Count == 0) return;

        var result = FileOperationService.Transfer(paths, dest, move: cut, Owner);
        if (result == FileOperationService.Result.Failed) ShowFailed();
        if (cut && result == FileOperationService.Result.Done) ClipboardService.Clear();

        // 貼り付けた項目を選択する(表示中のフォルダへ貼り付けた場合)
        await vm.RefreshAsync();
        if (destDir == null && result != FileOperationService.Result.Failed)
            SelectByPaths(paths.Select(p => Path.Combine(dest, Path.GetFileName(p.TrimEnd('\\')))));
    }

    // ───────── 他のペインへコピー / 移動 ─────────

    /// <summary>このペインと同じペインタブにある、ほかのペイン(ペインタブ以外では空)。</summary>
    private List<FolderTabViewModel> OtherPanes()
    {
        if (Vm == null || Application.Current.MainWindow?.DataContext is not MainViewModel { SelectedTab: PaneTabViewModel tab }
            || !tab.Panes.Contains(Vm)) return new();
        return tab.Panes.Where(p => p != Vm).ToList();
    }

    /// <summary>ショートカットの宛先。右隣のペイン(右端なら左端へ戻る)。</summary>
    private FolderTabViewModel? NextPane()
    {
        var vm = Vm;
        if (vm == null || Application.Current.MainWindow?.DataContext is not MainViewModel { SelectedTab: PaneTabViewModel tab }) return null;
        int i = tab.Panes.IndexOf(vm);
        return i < 0 || tab.Panes.Count < 2 ? null : tab.Panes[(i + 1) % tab.Panes.Count];
    }

    /// <summary>選択項目を、別のペインが表示しているフォルダへコピー/移動する。</summary>
    private async void TransferToPane(FolderTabViewModel? target, bool move)
    {
        var vm = Vm;
        var sel = Selected;
        if (vm == null || target == null || sel.Count == 0) return;

        string dest = target.Path;
        var paths = sel.Select(s => s.FullPath).ToList();
        if (!CanTransfer(paths, dest)) return;
        if (move) paths = paths.Where(p => !IsInFolder(p, dest)).ToList(); // 同じフォルダへの移動は何もしない
        if (paths.Count == 0) return;

        var result = FileOperationService.Transfer(paths, dest, move, Owner);
        if (result == FileOperationService.Result.Failed) ShowFailed();

        await vm.RefreshAsync();
        await target.RefreshAsync();
    }

    private static bool IsInFolder(string path, string folder) =>
        string.Equals(Path.GetDirectoryName(path.TrimEnd('\\')), folder.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);

    /// <summary>フォルダを自分自身の中へコピー/移動しようとしていないか確認する。</summary>
    private static bool CanTransfer(IEnumerable<string> sources, string dest)
    {
        if (sources.Any(s => Directory.Exists(s) && FileOperationService.IsInsideOrSame(s, dest)))
        {
            MessageBox.Show("フォルダを、そのフォルダ自身の中へはコピー・移動できません。", "KT_Explorer",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
        return true;
    }

    // ───────── 削除 ─────────

    private async void DeleteSelection(bool permanent)
    {
        var sel = Selected;
        if (sel.Count == 0 || Vm == null) return;

        // 標準のダイアログが確認を出す(ごみ箱へ/完全に削除)
        var result = FileOperationService.Delete(sel.Select(s => s.FullPath).ToList(), toRecycleBin: !permanent, Owner);
        if (result == FileOperationService.Result.Failed) ShowFailed();
        await Vm.RefreshAsync();
    }

    // ───────── 名前の変更(一覧上での直接編集) ─────────

    private FileEntry? _editing;

    /// <summary>選択中の1項目の名前を、一覧上で直接編集できる状態にする。</summary>
    private void RenameSelected()
    {
        if (FileList.SelectedItems.Count != 1 || FileList.SelectedItem is not FileEntry entry) return;
        StartRename(entry);
    }

    private void StartRename(FileEntry entry)
    {
        _renameTimer.Stop();
        if (_editing != null && _editing != entry) _editing.IsEditing = false;

        entry.EditName = Path.GetFileName(entry.FullPath.TrimEnd('\\')); // 検索結果では Name が相対パスなので、実際の名前を使う
        _editing = entry;
        FileList.ScrollIntoView(entry);
        entry.IsEditing = true; // 入力欄が表示され、IsVisibleChanged でフォーカス・選択される
    }

    private void EditBox_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is not true || sender is not TextBox box) return;

        // 表示直後はまだフォーカスを受けられないことがあるため、レイアウト後に行う
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            box.Focus();
            Keyboard.Focus(box);
            string text = box.Text;
            int dot = text.LastIndexOf('.');
            var entry = box.DataContext as FileEntry;
            // ファイルは、拡張子を除いた部分だけを選択する(エクスプローラーと同じ)
            if (entry is { IsDirectory: false } && dot > 0) box.Select(0, dot); else box.SelectAll();
        });
    }

    private void EditBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox { DataContext: FileEntry entry } box) return;
        if (e.Key == Key.Enter)
        {
            CommitRename(entry, box.Text);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            CancelRename(entry);
            e.Handled = true;
        }
    }

    // 他の場所をクリックしたときは、入力済みの名前を確定する(エクスプローラーと同じ)
    private void EditBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is TextBox { DataContext: FileEntry { IsEditing: true } entry } box)
            CommitRename(entry, box.Text);
    }

    // ファイル名に使えない文字は入力させず、理由を表示する
    private void EditBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (e.Text.IndexOfAny(Path.GetInvalidFileNameChars()) < 0) return;
        e.Handled = true;
        if (sender is TextBox box) ShowHint(box, "次の文字は使えません: \\ / : * ? \" < > |");
    }

    private void ShowHint(UIElement target, string text)
    {
        CloseHint();
        _hint = new System.Windows.Controls.ToolTip
        {
            Content = text,
            PlacementTarget = target,
            Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom,
            IsOpen = true,
        };
        _hintTimer.Stop();
        _hintTimer.Start();
    }

    private void CloseHint()
    {
        _hintTimer.Stop();
        if (_hint != null) { _hint.IsOpen = false; _hint = null; }
    }

    private void CancelRename(FileEntry entry)
    {
        CloseHint();
        entry.IsEditing = false;
        _editing = null;
        FileList.Focus();
    }

    private async void CommitRename(FileEntry entry, string newName)
    {
        if (!entry.IsEditing) return;     // Enter の確定に続いて、フォーカス喪失でも呼ばれるので二重実行を防ぐ
        CloseHint();
        entry.IsEditing = false;
        _editing = null;
        FileList.Focus();

        var vm = Vm;
        if (vm == null) return;
        string leaf = Path.GetFileName(entry.FullPath.TrimEnd('\\'));
        if (string.IsNullOrWhiteSpace(newName) || newName == leaf) return; // 変更なし(空は取り消し扱い)

        try
        {
            string newPath = FileOperationService.Rename(entry.FullPath, newName);
            SelectEntries(await vm.RefreshAsync(newPath));
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "名前の変更", MessageBoxButton.OK, MessageBoxImage.Warning);
            // 入力した名前を残したまま、もう一度編集できるようにする
            entry.EditName = newName;
            _editing = entry;
            entry.IsEditing = true;
        }
    }

    private static bool IsInEditBox(object? source) =>
        source is DependencyObject d && FindAncestor<TextBox>(d) is { DataContext: FileEntry };

    // 選択済みの名前をゆっくり2回クリックすると、名前の変更を始める(エクスプローラーと同じ)
    private void RenameTimer_Tick(object? sender, EventArgs e)
    {
        _renameTimer.Stop();
        var candidate = _renameCandidate;
        _renameCandidate = null;
        if (candidate != null && FileList.SelectedItems.Count == 1 && ReferenceEquals(FileList.SelectedItem, candidate))
            StartRename(candidate);
    }

    // ───────── 新規作成 ─────────

    /// <summary>表示中のフォルダに新しいフォルダ/テキストファイルを作り、続けて名前を付けさせる。</summary>
    private async void NewItem(bool folder)
    {
        var vm = Vm;
        if (vm == null) return;
        string path;
        try
        {
            path = folder
                ? FileOperationService.CreateFolder(vm.Path, "新しいフォルダー")
                : FileOperationService.CreateFile(vm.Path, "新しいテキスト ドキュメント", ".txt");
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "新規作成", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var entry = await vm.RefreshAsync(path);
        SelectEntries(entry);
        if (entry != null) StartRename(entry); // その場で名前を入力(Esc で既定の名前のまま)
    }

    // ───────── プロパティ / パスのコピー ─────────

    private static void ShowProperties(string path) => FolderActions.ShowProperties(path);

    private static void CopyPathText(IEnumerable<string> paths) => FolderActions.CopyPathText(paths);

    // ───────── 選択 ─────────

    private void SelectEntries(params FileEntry?[] entries)
    {
        FileList.SelectedItems.Clear();
        FileEntry? first = null;
        foreach (var e in entries)
        {
            if (e == null) continue;
            FileList.SelectedItems.Add(e);
            first ??= e;
        }
        if (first != null) FileList.ScrollIntoView(first);
        FileList.Focus();
    }

    private void SelectByPaths(IEnumerable<string> paths)
    {
        if (Vm == null) return;
        var set = new HashSet<string>(paths, StringComparer.OrdinalIgnoreCase);
        SelectEntries(Vm.Items.Where(i => set.Contains(i.FullPath)).ToArray());
    }

    private static void ShowFailed() =>
        MessageBox.Show("一部のファイルを処理できませんでした。", "KT_Explorer", MessageBoxButton.OK, MessageBoxImage.Warning);

    // ───────── 右クリックメニュー ─────────

    // 選択していない項目を右クリックしたら、その項目だけを選択する(標準と同じ)
    private void FileList_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        OnActivate(sender, e);
        if (IsInEditBox(e.OriginalSource)) return;
        var item = ItemAt(e.OriginalSource);
        if (item != null && !item.IsSelected)
        {
            FileList.SelectedItems.Clear();
            item.IsSelected = true;
        }
    }

    private void FileList_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        var vm = Vm;
        if (vm == null) return;
        if (IsInEditBox(e.OriginalSource)) return; // 入力欄では標準の(コピー/貼り付けなどの)メニューを使う

        var sel = Selected;
        bool onItem = sel.Count > 0 && ItemAt(e.OriginalSource) != null;

        // Shift+右クリック: 独自メニューを出さず、Windows 標準のメニューを(拡張項目つきで)直接出す
        if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0 && CanShowShellMenu(onItem, sel))
        {
            e.Handled = true;
            Dispatcher.BeginInvoke(DispatcherPriority.Background, () => ShowShellMenu(onItem, sel, extended: true));
            return;
        }

        bool canPaste = ClipboardService.HasFiles();
        var menu = new ContextMenu();

        MenuItem Add(string header, Action action, string? gesture = null, bool enabled = true, bool bold = false)
        {
            var item = new MenuItem { Header = header, IsEnabled = enabled, InputGestureText = gesture ?? "" };
            if (bold) item.FontWeight = FontWeights.Bold;
            item.Click += (_, _) => action();
            menu.Items.Add(item);
            return item;
        }

        if (onItem)
        {
            var single = sel.Count == 1 ? sel[0] : null;
            Add("開く", () => { if (single != null) vm.Open(single); }, "Enter", single != null, bold: true);
            if (single is { IsDirectory: true })
                Add("新しいタブで開く", () => (Application.Current.MainWindow?.DataContext as MainViewModel)?.OpenInNewTab(single.FullPath));
            menu.Items.Add(new Separator());
            Add("切り取り", () => CopySelection(cut: true), "Ctrl+X");
            Add("コピー", () => CopySelection(cut: false), "Ctrl+C");
            // フォルダを1つ選んでいるときは、そのフォルダの中へ貼り付ける
            Add("貼り付け", () => Paste(single is { IsDirectory: true } ? single.FullPath : null), "Ctrl+V", canPaste);
            AddPaneTransfer(menu, vm, Add);
            menu.Items.Add(new Separator());
            Add("名前の変更", RenameSelected, "F2", single != null);
            Add("削除", () => DeleteSelection(permanent: false), "Del");
            menu.Items.Add(new Separator());
            Add("パスをコピー", () => CopyPathText(sel.Select(s => s.FullPath)));
            if (single is { IsDirectory: true }) Add("ここでターミナルを開く", () => FolderActions.OpenTerminal(single.FullPath));
            Add("プロパティ", () => ShowProperties(sel[0].FullPath));
        }
        else
        {
            var create = new MenuItem { Header = "新規作成" };
            var newFolder = new MenuItem { Header = "フォルダー", InputGestureText = "Ctrl+Shift+N" };
            newFolder.Click += (_, _) => NewItem(folder: true);
            var newText = new MenuItem { Header = "テキスト ドキュメント" };
            newText.Click += (_, _) => NewItem(folder: false);
            create.Items.Add(newFolder);
            create.Items.Add(newText);
            menu.Items.Add(create);
            menu.Items.Add(new Separator());
            Add("貼り付け", () => Paste(null), "Ctrl+V", canPaste);
            Add("更新", vm.Reload, "F5");
            menu.Items.Add(new Separator());
            Add("パスをコピー", () => CopyPathText(new[] { vm.Path }));
            Add("ここでターミナルを開く", () => FolderActions.OpenTerminal(vm.Path));
            Add("プロパティ", () => ShowProperties(vm.Path));
        }

        // Windows 11 のエクスプローラーの「その他のオプションを確認」に相当する
        menu.Items.Add(new Separator());
        Add("その他のオプション", () =>
                Dispatcher.BeginInvoke(DispatcherPriority.Background, () => ShowShellMenu(onItem, sel, extended: false)),
            "Shift+右クリック", CanShowShellMenu(onItem, sel));

        // メニューは自分で開く。ContextMenu プロパティに設定して WPF に開かせると、WPF のメニュー表示処理は
        // こちらのハンドラーより先に走るため、1回目は何も出ず、2回目には前回の古いメニューが出てしまう。
        // (プロパティには設定しないので、次回以降も WPF 側の処理が二重に開くことはない)
        e.Handled = true;
        OpenMenu(menu, e);
    }

    /// <summary>ペインが複数あるとき、「(反対の)ペインへコピー/移動」を右クリックメニューへ加える。</summary>
    private void AddPaneTransfer(ContextMenu menu, FolderTabViewModel vm,
        Func<string, Action, string?, bool, bool, MenuItem> add)
    {
        var others = OtherPanes();
        if (others.Count == 0) return;
        menu.Items.Add(new Separator());
        if (others.Count == 1)
        {
            var target = others[0];
            add("反対のペインへコピー", () => TransferToPane(target, move: false), "Ctrl+Shift+C", true, false);
            add("反対のペインへ移動", () => TransferToPane(target, move: true), "Ctrl+Shift+M", true, false);
            return;
        }
        foreach (var target in others)
        {
            string name = string.IsNullOrEmpty(target.Title) ? target.Path : target.Title;
            add($"ペイン「{name}」へコピー", () => TransferToPane(target, move: false), null, true, false);
            add($"ペイン「{name}」へ移動", () => TransferToPane(target, move: true), null, true, false);
        }
    }

    private ContextMenu? _openMenu;

    private void OpenMenu(ContextMenu menu, ContextMenuEventArgs e)
    {
        if (_openMenu != null) _openMenu.IsOpen = false;
        menu.PlacementTarget = FileList;
        // キーボード(メニューキー / Shift+F10)で開いたときはマウスの位置が当てにならないので、中央に出す
        menu.Placement = e.CursorLeft < 0
            ? System.Windows.Controls.Primitives.PlacementMode.Center
            : System.Windows.Controls.Primitives.PlacementMode.MousePoint;
        _openMenu = menu;
        menu.IsOpen = true;
    }

    // ───────── Windows 標準(シェル)のコンテキストメニュー ─────────

    /// <summary>複数の項目を選んでいるとき、別々のフォルダにあるもの(検索結果など)にはシェルメニューを出せない。</summary>
    private static bool CanShowShellMenu(bool onItem, List<FileEntry> sel) =>
        !onItem || sel.Select(s => Path.GetDirectoryName(s.FullPath.TrimEnd('\\')))
                      .Distinct(StringComparer.OrdinalIgnoreCase).Count() == 1;

    private void ShowShellMenu(bool onItem, List<FileEntry> sel, bool extended)
    {
        var vm = Vm;
        if (vm == null) return;
        try
        {
            ShellContextMenu.Show(
                Owner,
                onItem ? sel.Select(s => s.FullPath).ToList() : new List<string>(),
                onItem ? null : vm.Path,
                extended,
                verb => HandleShellVerb(verb, onItem, sel));
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Windows 標準のメニューを表示できませんでした。\n{ex.Message}", "KT_Explorer",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>
    /// シェルのメニューで選ばれた動詞のうち、標準のエクスプローラーの画面を前提とするものを、KT_Explorer 自身の処理に置き換える。
    /// true を返すと、シェルには実行させない。
    /// </summary>
    private bool HandleShellVerb(string verb, bool onItem, List<FileEntry> sel)
    {
        var vm = Vm;
        if (vm == null) return false;
        var single = sel.Count == 1 ? sel[0] : null;

        switch (verb.ToLowerInvariant())
        {
            case "rename" when onItem && single != null:
                RenameSelected();                      // 一覧上での直接編集に置き換える
                return true;
            case "cut" when onItem:
                CopySelection(cut: true);
                return true;
            case "copy" when onItem:
                CopySelection(cut: false);
                return true;
            case "delete" when onItem:
                DeleteSelection(permanent: false);
                return true;
            case "paste":
                Paste(onItem && single is { IsDirectory: true } ? single.FullPath : null);
                return true;
            case "open" when onItem && single is { IsDirectory: true }:
                vm.Open(single);                       // 別のエクスプローラーを開かず、このタブで開く
                return true;
            case "refresh" when !onItem:
                vm.Reload();
                return true;
            default:
                return false;                          // それ以外(プロパティ、7-Zip、送る など)はシェルに任せる
        }
    }

    // ───────── ドラッグ&ドロップ(元) ─────────

    private Point _dragOrigin;
    private bool _dragArmed;
    private ListViewItem? _collapseItem;

    private void FileList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        OnActivate(sender, e);
        _renameTimer.Stop();
        _renameCandidate = null;
        if (IsInEditBox(e.OriginalSource)) { _dragArmed = false; return; } // 入力欄の中のクリック/ドラッグは文字選択に使う

        var item = ItemAt(e.OriginalSource);
        _dragArmed = item != null;
        _dragOrigin = e.GetPosition(null);
        _collapseItem = null;

        // すでに1つだけ選択されている項目の名前をクリックした → ゆっくり2回クリックでの名前の変更の候補
        bool onName = (e.OriginalSource as FrameworkElement)?.Tag as string == "NameText";
        if (item is { IsSelected: true, DataContext: FileEntry entry } && onName && e.ClickCount == 1
            && FileList.SelectedItems.Count == 1 && FileList.IsKeyboardFocusWithin && Keyboard.Modifiers == ModifierKeys.None)
            _renameCandidate = entry;

        // 複数選択のうちの1つを押した場合、押した時点で選択を1つに崩さない(複数まとめてドラッグできるように)。
        // ドラッグにならなかったときは、ボタンを離した時点で1つに絞る。
        if (item != null && item.IsSelected && FileList.SelectedItems.Count > 1 && Keyboard.Modifiers == ModifierKeys.None)
        {
            _collapseItem = item;
            FileList.Focus();
            e.Handled = true;
        }
    }

    private void FileList_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_collapseItem != null)
        {
            FileList.SelectedItems.Clear();
            _collapseItem.IsSelected = true;
            _collapseItem = null;
        }
        _dragArmed = false;

        // ダブルクリックの受付時間を過ぎても2回目のクリックが無ければ、名前の変更を始める
        if (_renameCandidate != null)
        {
            _renameTimer.Interval = TimeSpan.FromMilliseconds(GetDoubleClickTime() + 80);
            _renameTimer.Start();
        }
    }

    private void FileList_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || !_dragArmed) return;
        var delta = e.GetPosition(null) - _dragOrigin;
        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance) return;

        _dragArmed = false;
        _collapseItem = null;
        _renameCandidate = null;
        _renameTimer.Stop();
        var paths = Selected.Select(s => s.FullPath).ToArray();
        if (paths.Length == 0) return;

        // 標準のエクスプローラーなど、他のアプリへもドロップできる(FileDrop)
        DragDrop.DoDragDrop(FileList, new DataObject(DataFormats.FileDrop, paths), DragDropEffects.Copy | DragDropEffects.Move);
    }

    // ───────── ドラッグ&ドロップ(先) ─────────

    /// <summary>ドロップ先: フォルダの項目の上ならそのフォルダ、それ以外は表示中のフォルダ。</summary>
    private string? DropTarget(DragEventArgs e)
    {
        if (ItemAt(e.OriginalSource)?.DataContext is FileEntry { IsDirectory: true } dir) return dir.FullPath;
        return Vm?.Path;
    }

    private void FileList_DragOver(object sender, DragEventArgs e) => DropHelper.HandleDragOver(e, DropTarget(e));

    private void FileList_Drop(object sender, DragEventArgs e) => DropHelper.HandleDrop(e, DropTarget(e), Owner);

    // ───────── 補助 ─────────

    private static ListViewItem? ItemAt(object? source) =>
        source is DependencyObject d ? FindAncestor<ListViewItem>(d) : null;

    private static T? FindAncestor<T>(DependencyObject? d) where T : DependencyObject
    {
        while (d != null)
        {
            if (d is T t) return t;
            d = VisualTreeHelper.GetParent(d);
        }
        return null;
    }
}
