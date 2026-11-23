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

    public string SizeText => IsCalculating ? "計算中…" : Size is null ? "" : FormatSize(Size.Value);

    /// <summary>シェルアイコン(拡張子単位でキャッシュ)。</summary>
    public ImageSource? Icon => ShellIconService.GetIcon(FullPath, IsDirectory);

    private ImageSource? _thumbnail;
    private bool _thumbnailRequested;

    /// <summary>
    /// アイコン表示で使う、大きなアイコン・サムネイル。最初に参照されたときに、バックグラウンドで読み込み、
    /// 読み込めるまでは、小さなアイコンを返す(画面を止めない。アイコン表示でない間は、読み込まない)。
    /// </summary>
    public ImageSource? Thumbnail
    {
        get
        {
            if (!_thumbnailRequested)
            {
                _thumbnailRequested = true;
                _ = LoadThumbnailAsync();
            }
            return _thumbnail ?? Icon;
        }
    }

    private async Task LoadThumbnailAsync()
    {
        var image = await ThumbnailService.GetAsync(FullPath, 96);
        if (image == null) return;
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
