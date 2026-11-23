using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using KT_Explorer.Models;
using KT_Explorer.Services;

namespace KT_Explorer.ViewModels;

/// <summary>プレビューウィンドウの表示内容。選択中の項目に合わせて、バックグラウンドで読み込む。</summary>
public partial class PreviewViewModel : ObservableObject
{
    private const string NothingSelected = "ファイルを選ぶと、ここに内容が表示されます。";

    [ObservableProperty] private PreviewKind _kind = PreviewKind.None;
    [ObservableProperty] private string _title = "";
    [ObservableProperty] private string _info = NothingSelected;
    [ObservableProperty] private ImageSource? _image;
    [ObservableProperty] private string? _text;

    private readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(150) };
    private CancellationTokenSource? _cts;
    private FileEntry? _pending;

    public PreviewViewModel()
    {
        _debounce.Tick += (_, _) =>
        {
            _debounce.Stop();
            if (_pending != null) _ = LoadAsync(_pending);
        };
    }

    /// <summary>選択が変わった。1つだけ選んでいれば、その内容を表示する。矢印キーで素早く動かしたときは、止まってから読み込む。</summary>
    public void Show(IReadOnlyList<FileEntry> selection)
    {
        Cancel();
        if (selection.Count == 0) { SetNone(""); return; }
        if (selection.Count > 1) { SetNone($"{selection.Count} 個の項目を選択中です。"); return; }

        var entry = selection[0];
        Title = entry.Name;
        _pending = entry;
        _debounce.Stop();
        _debounce.Start();
    }

    /// <summary>プレビューを閉じたとき・タブを切り替えたとき。読み込み中のものを止めて、空にする。</summary>
    public void Clear()
    {
        Cancel();
        SetNone("");
    }

    private void Cancel()
    {
        _debounce.Stop();
        _pending = null;
        _cts?.Cancel();
        _cts = null;
    }

    private void SetNone(string message)
    {
        Kind = PreviewKind.None;
        Title = "";
        Image = null;
        Text = null;
        Info = message.Length > 0 ? message : NothingSelected;
    }

    private async Task LoadAsync(FileEntry entry)
    {
        var cts = new CancellationTokenSource();
        _cts = cts;
        PreviewContent content;
        try
        {
            string path = entry.FullPath;
            bool isDirectory = entry.IsDirectory;
            content = await Task.Run(() => PreviewLoader.Load(path, isDirectory, cts.Token), cts.Token);
        }
        catch (OperationCanceledException)
        {
            return; // 別の項目に切り替わった
        }
        if (cts.IsCancellationRequested || !ReferenceEquals(_cts, cts)) return;

        Kind = content.Kind;
        Image = content.Image;
        Text = content.Text;
        Info = content.Info;
    }
}
