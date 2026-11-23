using System.IO;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using KT_Explorer.Services;

namespace KT_Explorer.Models;

/// <summary>ファイル一覧の1行分。</summary>
public sealed partial class FileEntry : ObservableObject
{
    /// <summary>名前の直接編集中か(一覧の名前セルが入力欄に切り替わる)。</summary>
    [ObservableProperty] private bool _isEditing;

    /// <summary>直接編集中の入力内容。</summary>
    [ObservableProperty] private string _editName = "";

    /// <param name="displayName">検索結果で「検索起点からの相対パス」を出すときに指定する。</param>
    public FileEntry(FileSystemInfo info, string? displayName = null)
    {
        Name = displayName ?? info.Name;
        FullPath = info.FullName;
        IsDirectory = info is DirectoryInfo;
        Modified = info.LastWriteTime;
        Created = info.CreationTime;
        Attributes = info.Attributes;
        if (info is FileInfo fi)
        {
            try { Size = fi.Length; } catch { Size = null; }
        }
    }

    public string Name { get; }
    public string FullPath { get; }
    public bool IsDirectory { get; }

    private DateTime _modified;

    /// <summary>更新日時。フォルダは、中を開く(サイズの計算など)と、少し遅れて日時が更新されることがあるため、通知つきで更新できる。</summary>
    public DateTime Modified
    {
        get => _modified;
        set
        {
            if (SetProperty(ref _modified, value)) OnPropertyChanged(nameof(ModifiedText));
        }
    }

    private long? _size;

    /// <summary>ファイルの大きさ。フォルダは、サイズを計算したあとに入る(それまでは null)。</summary>
    public long? Size
    {
        get => _size;
        set
        {
            if (SetProperty(ref _size, value)) OnPropertyChanged(nameof(SizeText));
        }
    }

    /// <summary>フォルダのサイズを計算中か。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SizeText))]
    private bool _isCalculating;

    public string TypeText => IsDirectory
        ? "ファイル フォルダー"
        : (string.IsNullOrEmpty(Path.GetExtension(Name)) ? "ファイル" : Path.GetExtension(Name).TrimStart('.').ToUpperInvariant() + " ファイル");

    public string ModifiedText => Modified.ToString("yyyy/MM/dd HH:mm");

    /// <summary>作成日時。</summary>
    public DateTime Created { get; }

    public string CreatedText => Created.ToString("yyyy/MM/dd HH:mm");

    public FileAttributes Attributes { get; }

    /// <summary>属性を、1文字ずつで(R: 読み取り専用 / H: 隠し / S: システム / A: アーカイブ)。</summary>
    public string AttributesText => AttributeLetters(Attributes);

    public static string AttributeLetters(FileAttributes a)
    {
        var parts = new List<string>(4);
        if ((a & FileAttributes.ReadOnly) != 0) parts.Add("R");
        if ((a & FileAttributes.Hidden) != 0) parts.Add("H");
        if ((a & FileAttributes.System) != 0) parts.Add("S");
        if ((a & FileAttributes.Archive) != 0) parts.Add("A");
        return string.Join(' ', parts);
    }

    // ───────── 画像のサイズ・再生時間(列を表示したときだけ、バックグラウンドで読む) ─────────

    private MediaInfo? _media;
    private bool _mediaRequested;

    /// <summary>画像・動画のサイズ(「1920 × 1080」)。読み込めるまでは空。</summary>
    public string ImageSizeText { get { EnsureMedia(); return _media?.DimensionsText ?? ""; } }

    /// <summary>動画・音声の再生時間(「3:25」)。読み込めるまでは空。</summary>
    public string DurationText { get { EnsureMedia(); return _media?.DurationText ?? ""; } }

    private void EnsureMedia()
    {
        if (_mediaRequested || IsDirectory) return;
        _mediaRequested = true;
        if (!MediaInfoService.IsCandidate(FullPath)) return;
        if (MediaInfoService.TryGetCached(FullPath, Modified.Ticks, out var cached)) { _media = cached; return; }
        _ = LoadMediaAsync();
    }

    private async Task LoadMediaAsync()
    {
        var info = await MediaInfoService.GetAsync(FullPath, Modified.Ticks);
        _media = info;
        OnPropertyChanged(nameof(ImageSizeText));
        OnPropertyChanged(nameof(DurationText));
    }

    public string SizeText => IsCalculating ? "計算中…" : Size is null ? "" : FormatSize(Size.Value);

    /// <summary>シェルアイコン(拡張子単位でキャッシュ)。</summary>
    public ImageSource? Icon => ShellIconService.GetIcon(FullPath, IsDirectory);

    private ImageSource? _thumbnail;
    private int _thumbnailSize;

    /// <summary>
    /// アイコン表示で使う、大きなアイコン・サムネイル。最初に参照されたときに、バックグラウンドで読み込み、
    /// 読み込めるまでは、小さなアイコン(または、前の大きさのもの)を返す(画面を止めない)。
    /// アイコン表示でない間は、読み込まない。アイコンの大きさの設定が変わると、新しい大きさで読み直す。
    /// 一度読み込んだものは、メモリに記憶され(ThumbnailService)、別のフォルダへ移って戻っても、すぐに出る。
    /// </summary>
    public ImageSource? Thumbnail
    {
        get
        {
            int size = ThumbnailService.RequestSize;
            if (_thumbnailSize != size)
            {
                _thumbnailSize = size;
                if (ThumbnailService.TryGetCached(FullPath, size, Modified.Ticks, out var cached))
                    _thumbnail = cached ?? _thumbnail;
                else
                    _ = LoadThumbnailAsync(size);
            }
            return _thumbnail ?? Icon;
        }
    }

    private async Task LoadThumbnailAsync(int size)
    {
        var image = await ThumbnailService.GetAsync(FullPath, size, Modified.Ticks);
        if (image == null || size != _thumbnailSize) return; // 取得できなかった、または、その間に大きさが変わった
        _thumbnail = image;
        OnPropertyChanged(nameof(Thumbnail));
    }

    public static string FormatSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        double kb = bytes / 1024.0;
        if (kb < 1024) return $"{kb:N0} KB";
        double mb = kb / 1024.0;
        if (mb < 1024) return $"{mb:N1} MB";
        return $"{mb / 1024.0:N2} GB";
    }
}
