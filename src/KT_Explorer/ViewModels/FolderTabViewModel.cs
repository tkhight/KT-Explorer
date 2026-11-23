using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows.Data;
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
    private bool _disposed;
    private string? _itemsPath;      // Items が表している(通常一覧の)フォルダ

    public ObservableCollection<FileEntry> Items { get; } = new();

    /// <summary>Items にフィルタを掛けた表示用ビュー(ListView の既定ビューと同一)。</summary>
    private readonly ICollectionView _view;

    partial void OnFilterTextChanged(string value) => _view.Refresh();

    private bool MatchesFilter(object o) =>
        string.IsNullOrEmpty(FilterText) ||
        (o is FileEntry e && e.Name.Contains(FilterText, StringComparison.CurrentCultureIgnoreCase));

    public override string ToolTipText => Path;

    /// <summary>表示フォルダが変わったとき(ツリー連動用)。</summary>
    public event Action<FolderTabViewModel>? PathChanged;

    /// <summary>ペイン内でユーザーが操作を始めた(ペインタブの操作対象切替用)。</summary>
    public event Action<FolderTabViewModel>? Activated;

    public void RequestActivate() => Activated?.Invoke(this);

    /// <summary>項目を開く。フォルダなら移動、ファイルなら関連付けで起動。</summary>
    public void Open(FileEntry entry)
    {
        if (entry.IsDirectory)
        {
            NavigateTo(entry.FullPath);
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
            if (!_disposed && !_showingSearch) _ = LoadAsync(Path);
        };

        NavigateTo(path, recordHistory: false);
    }

    /// <summary>一覧を今すぐ読み直す。selectPath があれば、読み直し後にその項目を返す(選択用)。</summary>
    public async Task<FileEntry?> RefreshAsync(string? selectPath = null)
    {
        await LoadAsync(Path);
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
        if (_view is ListCollectionView lcv) lcv.CustomSort = SortSettings.Instance.CreateComparer();
    }

    public override void Dispose()
    {
        _disposed = true;
        SortSettings.Instance.Changed -= ApplySort;
        _refreshTimer.Stop();
        DisposeWatcher();
        CancelSearch();
    }

    public void NavigateTo(string path, bool recordHistory = true)
    {
        string normalized = Normalize(path);
        if (string.Equals(normalized, Path, StringComparison.OrdinalIgnoreCase)) { Reload(); return; }

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
            int total = await Task.Run(() => SearchCore(root, query, progress, cts.Token), cts.Token);
            if (!cts.IsCancellationRequested)
                Status = total >= MaxSearchResults ? $"{total} 件(上限に達したため打ち切り)" : $"{total} 件";
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

    private static int SearchCore(string root, string query, IProgress<(List<FileEntry> Batch, int Folders)> progress, CancellationToken ct)
    {
        var match = MakeMatcher(query);
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

                if (match(info.Name))
                {
                    batch.Add(new FileEntry(info, System.IO.Path.GetRelativePath(root, info.FullName)));
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
        string leaf = System.IO.Path.GetFileName(path.TrimEnd('\\', '/'));
        Title = string.IsNullOrEmpty(leaf) ? path : leaf;
        CanGoBack = _back.Count > 0;
        CanGoForward = _forward.Count > 0;
        CanGoUp = Directory.GetParent(path) != null;
        PathChanged?.Invoke(this);
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
        }
        else
        {
            Items.Clear();
            foreach (var e in entries) Items.Add(e);
        }
        _itemsPath = path;
        Error = error;
    }

    /// <summary>Items を fresh と同じ並びにする。変化の無い項目はそのまま残し、選択が消えないようにする。</summary>
    private void SyncItems(List<FileEntry> fresh)
    {
        var freshByPath = fresh.ToDictionary(e => e.FullPath, StringComparer.OrdinalIgnoreCase);

        // 消えた項目・更新された項目(日時/サイズ/種類が違う)を外す
        for (int i = Items.Count - 1; i >= 0; i--)
        {
            var old = Items[i];
            if (!freshByPath.TryGetValue(old.FullPath, out var f) ||
                f.Modified != old.Modified || f.Size != old.Size || f.IsDirectory != old.IsDirectory)
                Items.RemoveAt(i);
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
