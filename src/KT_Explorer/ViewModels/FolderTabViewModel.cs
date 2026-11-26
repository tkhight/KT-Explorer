using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows.Data;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using KT_Explorer.Models;
using KT_Explorer.Services;

namespace KT_Explorer.ViewModels;

/// <summary>1つのタブ(1つのフォルダ表示と、その戻る/進む履歴)。</summary>
public partial class FolderTabViewModel : TabBase
{
    private readonly List<string> _back = new();
    private readonly List<string> _forward = new();
    private int _loadVersion;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ToolTipText))]
    private string _path = "";
    [ObservableProperty] private bool _canGoBack;
    [ObservableProperty] private bool _canGoForward;
    [ObservableProperty] private bool _canGoUp;
    [ObservableProperty] private string? _error;
    [ObservableProperty] private bool _isActivePane;

    /// <summary>一覧の絞り込み(名前の部分一致)。フォルダを移動するとクリアされる。</summary>
    [ObservableProperty] private string _filterText = "";

    /// <summary>再帰検索の入力。Enter で実行する。フォルダを移動するとクリアされる。</summary>
    [ObservableProperty] private string _searchText = "";

    /// <summary>検索の進行/結果件数の表示。</summary>
    [ObservableProperty] private string? _status;

    private CancellationTokenSource? _searchCts;
    private const int MaxSearchResults = 3000;

    // フォルダの変更を監視して、一覧を自動で更新する(ファイル操作後や外部での変更)
    private FileSystemWatcher? _watcher;
    private readonly System.Windows.Threading.DispatcherTimer _refreshTimer;
    private bool _showingSearch;     // 一覧が検索結果のときは自動更新しない

    /// <summary>一覧が検索結果か。</summary>
    public bool IsShowingSearch => _showingSearch;
    private bool _disposed;
    private string? _itemsPath;      // Items が表している(通常一覧の)フォルダ

    public ObservableCollection<FileEntry> Items { get; } = new();

    /// <summary>一覧で選択中の項目(プレビューの表示に使う)。</summary>
    public IReadOnlyList<FileEntry> Selection { get; private set; } = Array.Empty<FileEntry>();

    /// <summary>選択が変わった。</summary>
    public event Action? SelectionChanged;

    public void SetSelection(IReadOnlyList<FileEntry> selection)
    {
        Selection = selection;
        SelectionChanged?.Invoke();
    }

    /// <summary>このタブの並べ替えの列と向き。タブごとに持ち、状態ファイルに保存する。</summary>
    public SortColumn SortColumn { get; private set; } = SortSettings.Instance.Column;
    public bool SortDescending { get; private set; } = SortSettings.Instance.Descending;

    /// <summary>並べ替えの列・向きが変わった(列見出しの ▲▼ の更新用)。</summary>
    public event Action? SortChanged;

    /// <summary>列見出しのクリック。別の列なら昇順で、同じ列なら昇順/降順を入れ替える。新しいタブの初期の並び順としても覚える。</summary>
    public void ToggleSort(SortColumn column)
    {
        if (column == SortColumn) SortDescending = !SortDescending;
        else { SortColumn = column; SortDescending = false; }
        SortSettings.Instance.SetDefault(SortColumn, SortDescending);
        ApplySort();
        SortChanged?.Invoke();
    }

    /// <summary>このタブの表示(詳細・アイコン・一覧)。タブごとに持ち、状態ファイルに保存する。</summary>
    public ViewMode ViewMode { get; private set; } = SortSettings.Instance.DefaultViewMode;

    /// <summary>表示が変わった(一覧の見た目の切り替え用)。</summary>
    public event Action? ViewModeChanged;

    /// <summary>メニューなどから「新規作成」を頼まれた(一覧の画面が、作って、名前の入力まで進める)。引数: フォルダーなら true、テキスト ドキュメントなら false。</summary>
    public event Action<bool>? NewItemRequested;

    public void RequestNewItem(bool folder) => NewItemRequested?.Invoke(folder);

    /// <summary>メニュー(編集)から、切り取り・コピー・貼り付け・削除・全て選択を頼まれた(一覧の画面が実行する)。</summary>
    public event Action<EditCommand>? EditRequested;

    public void RequestEdit(EditCommand command) => EditRequested?.Invoke(command);

    /// <summary>表示を切り替える。remember が true なら、新しく開くタブの初期の表示としても覚える(保存の復元では false)。</summary>
    public void SetViewMode(ViewMode mode, bool remember = true)
    {
        if (remember) SortSettings.Instance.DefaultViewMode = mode;
        if (ViewMode == mode) return;
        ViewMode = mode;
        ViewModeChanged?.Invoke();
    }

    /// <summary>並べ替えの列と向きを、メニューから直接選ぶ(アイコン・一覧には列見出しが無いため)。新しいタブの初期の並び順としても覚える。</summary>
    public void ChooseSort(SortColumn column, bool descending)
    {
        SortColumn = column;
        SortDescending = descending;
        SortSettings.Instance.SetDefault(column, descending);
        ApplySort();
        SortChanged?.Invoke();
    }

    /// <summary>保存された並び順を復元する(新しいタブの初期の並び順は変えない)。</summary>
    public void SetSort(SortColumn column, bool descending)
    {
        SortColumn = column;
        SortDescending = descending;
        ApplySort();
        SortChanged?.Invoke();
    }

    /// <summary>ペインとして並べたときの幅の比率(0 は未設定)。ペインの増減や、スプリッターのドラッグのあとも保つ。</summary>
    public double PaneWeight { get; set; }

    /// <summary>Items にフィルタを掛けた表示用ビュー(ListView の既定ビューと同一)。</summary>
    private readonly ICollectionView _view;

    /// <summary>絞り込みの件数の表示(「絞り込み: 12 / 40 件」)。絞り込んでいないときは空。</summary>
    [ObservableProperty] private string? _filterNote;

    /// <summary>絞り込みの条件のうち、解釈できなかったもの(その条件は無視される)。</summary>
    [ObservableProperty] private string? _filterProblem;

    private FilterQuery _query = FilterQuery.Empty;

    partial void OnFilterTextChanged(string value)
    {
        _query = FilterQuery.Parse(value, DateTime.Now);
        _view.Refresh();
        UpdateFilterNote();
    }

    private bool MatchesFilter(object o) => o is FileEntry e && (_query.IsEmpty || _query.Matches(e));

    /// <summary>絞り込み中の件数と、解釈できなかった条件を、表示用に更新する。</summary>
    private void UpdateFilterNote()
    {
        if (string.IsNullOrWhiteSpace(FilterText))
        {
            FilterNote = null;
            FilterProblem = null;
            return;
        }
        int shown = _view.Cast<object>().Count();
        FilterNote = _query.IsEmpty ? null : $"絞り込み: {shown} / {Items.Count} 件";
        FilterProblem = _query.Problems.Count == 0 ? null : string.Join("、", _query.Problems);
    }

    public override string ToolTipText => Path;

    /// <summary>表示フォルダが変わったとき(ツリー連動用)。</summary>
    public event Action<FolderTabViewModel>? PathChanged;

    /// <summary>ペイン内でユーザーが操作を始めた(ペインタブの操作対象切替用)。</summary>
    public event Action<FolderTabViewModel>? Activated;

    public void RequestActivate() => Activated?.Invoke(this);

    /// <summary>
    /// 項目を開く。フォルダなら移動、ファイルなら関連付けで起動。
    /// リンク先がフォルダのショートカット(.lnk)は、エクスプローラーを開かず、リンク先のフォルダへ、この一覧のまま移動する。
    /// </summary>
    public void Open(FileEntry entry)
    {
        if (entry.IsDirectory)
        {
            NavigateTo(entry.FullPath);
            return;
        }
        if (ShortcutService.TryResolveFolder(entry.FullPath, out var target))
        {
            NavigateTo(target);
            return;
        }
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(entry.FullPath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(ex.Message, "KT_Explorer");
        }
    }

    private OverlayLoader? _overlayLoader;
    private OverlayLoader TabOverlay => _overlayLoader ??= new OverlayLoader(() => Path, () => OnPropertyChanged(nameof(OverlayIcon)));

    /// <summary>タブの左に出す、フォルダのアイコン(通常は共通のフォルダのアイコン。ドライブ・デスクトップなどは固有のもの)。</summary>
    public override ImageSource? Icon => string.IsNullOrEmpty(Path) ? null : ShellIconService.GetIcon(Path, isDirectory: true);

    /// <summary>フォルダのオーバーレイ(OneDrive・SVN などの状態)。表示されたとき、バックグラウンドで調べる。</summary>
    public override ImageSource? OverlayIcon => string.IsNullOrEmpty(Path) ? null : TabOverlay.Small;

    /// <summary>オーバーレイをいったん消して、調べ直す(「オーバーレイを表示」の切り替え)。</summary>
    public void ResetTabOverlay() => _overlayLoader?.Reset();

    public FolderTabViewModel(string path)
    {
        _view = CollectionViewSource.GetDefaultView(Items);
        _view.Filter = MatchesFilter;
        ApplySort();
        SortSettings.Instance.Changed += ApplySort;

        // 変更通知が連続しても1回にまとめて更新する
        _refreshTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _refreshTimer.Tick += (_, _) =>
        {
            _refreshTimer.Stop();
            if (_disposed) return;
            FolderSizeCache.InvalidateUp(Path); // このフォルダの中身が変わった: このフォルダと、上位のフォルダのサイズの記憶は古い
            if (!_showingSearch) _ = LoadAsync(Path);
        };

        NavigateTo(path, recordHistory: false);
        _recordVisits = true; // 作った(復元した)ときの場所は、開いた履歴に数えない
    }

    private bool _recordVisits;

    /// <summary>一覧を今すぐ読み直す。selectPath があれば、読み直し後にその項目を返す(選択用)。</summary>
    public async Task<FileEntry?> RefreshAsync(string? selectPath = null)
    {
        // 検索結果を表示中は、通常の一覧に置き換えず、同じ検索をやり直して、結果に反映する
        if (_showingSearch && !string.IsNullOrWhiteSpace(SearchText)) await SearchAsync();
        else await LoadAsync(Path);
        return selectPath == null
            ? null
            : Items.FirstOrDefault(e => string.Equals(e.FullPath, selectPath, StringComparison.OrdinalIgnoreCase));
    }

    // 監視の通知は別スレッドから来るので、このタブを作った(UI)スレッドへ戻して処理する
    private readonly System.Windows.Threading.Dispatcher _dispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;

    private void ScheduleRefresh() =>
        _dispatcher.BeginInvoke(() =>
        {
            if (_disposed) return;
            _refreshTimer.Stop();
            _refreshTimer.Start();
        });

    private void ResetWatcher(string path)
    {
        DisposeWatcher();
        try
        {
            var w = new FileSystemWatcher(path)
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite
                               | NotifyFilters.Size | NotifyFilters.Attributes,
                IncludeSubdirectories = false,
            };
            w.Created += (_, _) => ScheduleRefresh();
            w.Deleted += (_, _) => ScheduleRefresh();
            w.Changed += (_, _) => ScheduleRefresh();
            w.Renamed += (_, _) => ScheduleRefresh();
            w.Error += (_, _) => ScheduleRefresh();   // バッファあふれなど: 取りこぼしても読み直せば整う
            w.EnableRaisingEvents = true;
            _watcher = w;
        }
        catch
        {
            _watcher = null; // 監視できないフォルダ(存在しない・権限なし)は手動更新(F5)のみ
        }
    }

    /// <summary>
    /// フォルダの監視を止める。監視中のフォルダやその親は、Windows の仕組み上、名前の変更・削除ができないため、
    /// それらの操作の前に止め、終わったら <see cref="ResumeWatching"/> で再開する。
    /// </summary>
    public void PauseWatching() => DisposeWatcher();

    public void ResumeWatching()
    {
        if (!_disposed && _watcher == null && Directory.Exists(Path)) ResetWatcher(Path);
    }

    private void DisposeWatcher()
    {
        if (_watcher == null) return;
        _watcher.EnableRaisingEvents = false;
        _watcher.Dispose();
        _watcher = null;
    }

    /// <summary>
    /// 並び順を表示用ビューに適用する。Items 自体は名前順のままなので、選択状態や差分更新に影響しない
    /// (検索結果の一覧にも同じ並び順がかかる)。
    /// </summary>
    private void ApplySort()
    {
        if (_view is ListCollectionView lcv) lcv.CustomSort = SortSettings.Instance.CreateComparer(SortColumn, SortDescending);
    }

    public override void Dispose()
    {
        _disposed = true;
        SourceGroup = null; // グループの変更の通知を外す
        CancelFolderSizes();
        SortSettings.Instance.Changed -= ApplySort;
        _refreshTimer.Stop();
        DisposeWatcher();
        CancelSearch();
    }

    /// <summary>ピン留めされたタブが、別のフォルダへの移動を頼まれた。移動はせず、移動先を新しいタブで開いてもらう(引数: 移動先)。</summary>
    public event Action<FolderTabViewModel, string>? NewTabRequested;

    /// <summary>
    /// フォルダへ移動する。ピン留めされたタブでは、ユーザーの操作による別のフォルダへの移動(recordHistory が true)は、
    /// このタブを動かさず、移動先を新しいタブで開く。名前の変更への追従などの内部の移動(recordHistory が false)は、そのまま移動する。
    /// </summary>
    public void NavigateTo(string path, bool recordHistory = true)
    {
        string normalized = Normalize(path);
        if (string.Equals(normalized, Path, StringComparison.OrdinalIgnoreCase)) { Reload(); return; }
        if (IsPinned && recordHistory && NewTabRequested != null)
        {
            NewTabRequested.Invoke(this, normalized);
            return;
        }

        if (recordHistory && !string.IsNullOrEmpty(Path))
        {
            _back.Add(Path);
            _forward.Clear();
        }
        SetPath(normalized);
    }

    public void GoBack()
    {
        if (_back.Count == 0) return;
        _forward.Add(Path);
        string target = _back[^1];
        _back.RemoveAt(_back.Count - 1);
        SetPath(target);
    }

    public void GoForward()
    {
        if (_forward.Count == 0) return;
        _back.Add(Path);
        string target = _forward[^1];
        _forward.RemoveAt(_forward.Count - 1);
        SetPath(target);
    }

    public void GoUp()
    {
        var parent = Directory.GetParent(Path);
        if (parent != null) NavigateTo(parent.FullName);
    }

    /// <summary>一覧を読み直す。検索結果表示中なら通常の一覧に戻る(絞り込みは保持)。</summary>
    public void Reload()
    {
        CancelSearch();
        SearchText = "";
        _ = LoadAsync(Path);
    }

    // ───────── 再帰検索 ─────────

    /// <summary>現在のフォルダ以下を再帰検索し、結果を一覧に表示する。空入力なら通常の一覧に戻る。</summary>
    public async Task SearchAsync()
    {
        string query = SearchText.Trim();
        if (query.Length == 0) { Reload(); return; }

        CancelSearch();
        var cts = new CancellationTokenSource();
        _searchCts = cts;
        _showingSearch = true;
        _loadVersion++; // 読み込み中の通常一覧が後から結果を上書きしないようにする
        Error = null;
        Items.Clear();
        _itemsPath = null;
        Status = "検索中… 0 件";

        string root = Path;
        var progress = new Progress<(List<FileEntry> Batch, int Folders)>(p =>
        {
            if (cts.IsCancellationRequested) return;
            foreach (var e in p.Batch) Items.Add(e);
            Status = $"検索中… {Items.Count} 件({p.Folders} フォルダを確認)";
        });

        try
        {
            var parsed = FilterQuery.Parse(query, DateTime.Now, allowContent: true);
            if (parsed.IsEmpty)
            {
                Status = parsed.Problems.Count > 0 ? string.Join(" / ", parsed.Problems) : "検索条件がありません";
                return;
            }
            int total = await Task.Run(() => SearchCore(root, parsed, progress, cts.Token), cts.Token);
            if (!cts.IsCancellationRequested)
            {
                string note = parsed.Problems.Count > 0 ? "  ※" + string.Join(" / ", parsed.Problems) : "";
                Status = (total >= MaxSearchResults ? $"{total} 件(上限に達したため打ち切り)" : $"{total} 件") + note;
            }
        }
        catch (OperationCanceledException)
        {
            // 新しい検索や移動で中断された
        }
    }

    /// <summary>実行中の検索を中断し、検索表示を消す。</summary>
    public void CancelSearch()
    {
        _searchCts?.Cancel();
        _searchCts = null;
        _showingSearch = false;
        Status = null;
    }

    internal static int SearchCore(string root, FilterQuery query, IProgress<(List<FileEntry> Batch, int Folders)> progress, CancellationToken ct)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        var batch = new List<FileEntry>();
        int total = 0, folders = 0;
        var clock = System.Diagnostics.Stopwatch.StartNew();

        // 件数が少なくても画面が止まって見えないよう、一定時間ごとに途中経過を出す
        void Flush()
        {
            progress.Report((batch, folders));
            batch = new List<FileEntry>();
            clock.Restart();
        }

        while (pending.Count > 0 && total < MaxSearchResults)
        {
            ct.ThrowIfCancellationRequested();
            List<FileSystemInfo> entries;
            try { entries = new DirectoryInfo(pending.Pop()).EnumerateFileSystemInfos().ToList(); }
            catch { continue; } // アクセス不可のフォルダは飛ばす
            folders++;

            foreach (var info in entries)
            {
                ct.ThrowIfCancellationRequested();
                if ((info.Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0) continue;

                var entry = new FileEntry(info, System.IO.Path.GetRelativePath(root, info.FullName));
                if (query.Matches(entry, ct))
                {
                    batch.Add(entry);
                    total++;
                    if (total >= MaxSearchResults) break;
                }
                // ジャンクション等のループを避けるため、リパースポイントは辿らない
                if (info is DirectoryInfo && (info.Attributes & FileAttributes.ReparsePoint) == 0)
                    pending.Push(info.FullName);
            }
            if (batch.Count >= 50 || clock.ElapsedMilliseconds >= 250) Flush();
        }
        Flush();
        return total;
    }

    /// <summary>* や ? を含めばワイルドカード、含まなければ部分一致。</summary>
    private static Func<string, bool> MakeMatcher(string query)
    {
        if (query.IndexOfAny(new[] { '*', '?' }) < 0)
            return name => name.Contains(query, StringComparison.CurrentCultureIgnoreCase);

        var regex = new Regex("^" + Regex.Escape(query).Replace("\\*", ".*").Replace("\\?", ".") + "$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return name => regex.IsMatch(name);
    }

    private void SetPath(string path)
    {
        CancelSearch();
        SearchText = "";
        FilterText = "";
        Path = path;
        OnPropertyChanged(nameof(Icon));
        _overlayLoader?.Reset(); // 別のフォルダになったので、オーバーレイも調べ直す
        string leaf =System.IO.Path.GetFileName(path.TrimEnd('\\', '/'));
        Title = string.IsNullOrEmpty(leaf) ? path : leaf;
        CanGoBack = _back.Count > 0;
        CanGoForward = _forward.Count > 0;
        CanGoUp = Directory.GetParent(path) != null;
        PathChanged?.Invoke(this);
        if (_recordVisits) FolderHistory.Record(path); // 開いたフォルダの履歴(戻る・進むも含む)
        CancelFolderSizes();
        ResetWatcher(path);
        _ = LoadAsync(path);
    }

    private async Task LoadAsync(string path)
    {
        int version = ++_loadVersion;
        List<FileEntry> entries;
        string? error = null;
        try
        {
            entries = await Task.Run(() => Read(path));
        }
        catch (Exception ex)
        {
            entries = new List<FileEntry>();
            error = ex.Message;
        }
        if (version != _loadVersion || _disposed) return; // 古い読み込み結果は捨てる

        if (_itemsPath != null && string.Equals(_itemsPath, path, StringComparison.OrdinalIgnoreCase))
        {
            SyncItems(entries); // 同じフォルダの再読み込みは差分だけ反映(選択状態を保つ)
            _overlayLoader?.Refresh(); // このフォルダ自身の状態(タブのオーバーレイ)も調べ直す
        }
        else
        {
            Items.Clear();
            foreach (var e in entries) Items.Add(e);
        }
        _itemsPath = path;
        Error = error;
        PrefillFolderSizes(entries);
        UpdateFilterNote();
        if (FolderSizeSettings.Auto) CalculateAllFolderSizes();
    }

    // ───────── フォルダのサイズ計算 ─────────

    private CancellationTokenSource _sizeCts = new();
    private readonly SemaphoreSlim _sizeLock = new(1, 1); // 1つずつ順に計算する(ディスクやネットワークを同時に叩かない)

    /// <summary>計算済み(記憶してある)のフォルダのサイズは、計算し直さず、すぐに表示する(別のフォルダへ移って戻ったときなど)。</summary>
    private static void PrefillFolderSizes(IEnumerable<FileEntry> entries)
    {
        foreach (var e in entries)
            if (e.IsDirectory && e.Size == null && FolderSizeCache.TryGet(e.FullPath, out long size)) e.Size = size;
    }

    /// <summary>一覧のすべてのフォルダのサイズを、バックグラウンドで計算する。</summary>
    public void CalculateAllFolderSizes() => CalculateFolderSizes(Items.Where(e => e.IsDirectory).ToList());

    /// <summary>
    /// 指定したフォルダのサイズを、バックグラウンドで順に計算する。計算中のものは飛ばす。
    /// recalculate が false なら、計算済みのものも飛ばす(自動計算用)。true なら、計算し直す(右クリックでの手動計算用)。
    /// </summary>
    public void CalculateFolderSizes(IEnumerable<FileEntry> folders, bool recalculate = false)
    {
        var ct = _sizeCts.Token;
        foreach (var entry in folders.Where(e => e.IsDirectory && !e.IsCalculating && (recalculate || e.Size == null)).ToList())
        {
            entry.IsCalculating = true;
            _ = CalculateOneAsync(entry, ct, useCache: !recalculate);
        }
    }

    private async Task CalculateOneAsync(FileEntry entry, CancellationToken ct, bool useCache)
    {
        try
        {
            await _sizeLock.WaitAsync(ct);
            try
            {
                if (useCache && FolderSizeCache.TryGet(entry.FullPath, out long cached))
                {
                    if (!ct.IsCancellationRequested) entry.Size = cached;
                }
                else
                {
                    long size = await Task.Run(() => FolderSizeService.Calculate(entry.FullPath, ct), ct);
                    if (!ct.IsCancellationRequested)
                    {
                        entry.Size = size;
                        FolderSizeCache.Set(entry.FullPath, size);
                    }
                }
            }
            finally
            {
                _sizeLock.Release();
            }
        }
        catch (OperationCanceledException)
        {
            // 別のフォルダへ移動したので、計算をやめた
        }
        catch (Exception)
        {
            // 計算に失敗したら(権限など)、サイズは空のままにする
        }
        finally
        {
            entry.IsCalculating = false;
        }
        // サイズで並べているときや、サイズで絞り込んでいるときは、計算できた値で並べ直す・絞り直す
        if (!ct.IsCancellationRequested && (SortColumn == SortColumn.Size || !_query.IsEmpty))
        {
            _view.Refresh();
            UpdateFilterNote();
        }
    }

    /// <summary>実行中・待機中のサイズ計算をすべて中断する(別のフォルダへ移動したとき、閉じたとき)。</summary>
    private void CancelFolderSizes()
    {
        _sizeCts.Cancel();
        _sizeCts = new CancellationTokenSource();
    }

    /// <summary>Items を fresh と同じ並びにする。変化の無い項目はそのまま残し、選択が消えないようにする。</summary>
    private void SyncItems(List<FileEntry> fresh)
    {
        var freshByPath = fresh.ToDictionary(e => e.FullPath, StringComparer.OrdinalIgnoreCase);

        // 消えた項目・更新された項目(日時/サイズ/種類が違う)を外す
        for (int i = Items.Count - 1; i >= 0; i--)
        {
            var old = Items[i];
            if (!freshByPath.TryGetValue(old.FullPath, out var f) || f.IsDirectory != old.IsDirectory)
            {
                Items.RemoveAt(i);
            }
            else if (f.IsDirectory)
            {
                // フォルダは、更新日時だけが変わっても、項目を作り直さない。サイズを計算すると(中を開くと)、
                // NTFS がフォルダの更新日時を少し遅れて更新するため、作り直すと、計算したサイズが消えてしまう。
                if (f.Modified != old.Modified) old.Modified = f.Modified;
                old.RefreshOverlay();
            }
            else if (f.Modified != old.Modified || f.Size != old.Size)
            {
                Items.RemoveAt(i);
            }
            else
            {
                old.RefreshOverlay(); // 変わっていないファイルも、同期の状態などは変わっているかもしれない
            }
        }

        // 残りは fresh の部分列(同じ並び順)なので、足りない項目を位置に挿入していけばよい
        for (int i = 0; i < fresh.Count; i++)
        {
            if (i < Items.Count && string.Equals(Items[i].FullPath, fresh[i].FullPath, StringComparison.OrdinalIgnoreCase))
                continue;
            Items.Insert(i, fresh[i]);
        }
    }

    private static List<FileEntry> Read(string path)
    {
        var dir = new DirectoryInfo(path);
        var list = new List<FileEntry>();
        foreach (var info in dir.EnumerateFileSystemInfos())
        {
            if ((info.Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0) continue;
            list.Add(new FileEntry(info));
        }
        list.Sort((a, b) =>
        {
            if (a.IsDirectory != b.IsDirectory) return a.IsDirectory ? -1 : 1;
            return string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase);
        });
        return list;
    }

    public static string Normalize(string path)
    {
        string full = System.IO.Path.GetFullPath(path.Trim().Trim('"'));
        string root = System.IO.Path.GetPathRoot(full) ?? "";
        return string.Equals(full, root, StringComparison.OrdinalIgnoreCase) ? root : full.TrimEnd('\\');
    }
}

/// <summary>編集メニューから一覧へ頼む操作。</summary>
public enum EditCommand
{
    Cut,
    Copy,
    Paste,
    Delete,
    SelectAll,
}
