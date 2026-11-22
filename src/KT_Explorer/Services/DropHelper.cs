using System.IO;
using System.Windows;

namespace KT_Explorer.Services;

/// <summary>ファイルのドラッグ&ドロップ(一覧・ツリー・タブ共通)。標準のエクスプローラーとの間でも使える。</summary>
public static class DropHelper
{
    public static string[]? GetFiles(IDataObject data) =>
        data.GetDataPresent(DataFormats.FileDrop) ? data.GetData(DataFormats.FileDrop) as string[] : null;

    /// <summary>
    /// ドロップの効果を決める。Ctrl=コピー / Shift=移動 / 指定なし=同じドライブなら移動、違えばコピー(標準と同じ)。
    /// 自分自身の中へのドロップや、同じ場所への移動は None。
    /// </summary>
    public static DragDropEffects Decide(DragEventArgs e, string dest, string[] sources)
    {
        if (sources.Length == 0 || !Directory.Exists(dest)) return DragDropEffects.None;
        if (sources.Any(s => Directory.Exists(s) && FileOperationService.IsInsideOrSame(s, dest))) return DragDropEffects.None;

        bool ctrl = (e.KeyStates & DragDropKeyStates.ControlKey) != 0;
        bool shift = (e.KeyStates & DragDropKeyStates.ShiftKey) != 0;
        var effect = ctrl ? DragDropEffects.Copy
                   : shift ? DragDropEffects.Move
                   : sources.All(s => FileOperationService.IsSameVolume(s, dest)) ? DragDropEffects.Move
                   : DragDropEffects.Copy;

        // 同じフォルダへの移動は意味がない
        if (effect == DragDropEffects.Move &&
            sources.All(s => string.Equals(Path.GetDirectoryName(s.TrimEnd('\\')), dest.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)))
            return DragDropEffects.None;

        effect &= e.AllowedEffects; // ドラッグ元が許していない操作は使わない
        return effect;
    }

    /// <summary>DragOver 用。ファイルのドラッグなら効果を設定して処理済みにする。</summary>
    public static void HandleDragOver(DragEventArgs e, string? dest)
    {
        var files = GetFiles(e.Data);
        e.Effects = files == null || dest == null ? DragDropEffects.None : Decide(e, dest, files);
        e.Handled = true;
    }

    /// <summary>Drop 用。進捗ダイアログ表示中にドラッグ操作が止まらないよう、実行は Drop の処理が終わってから行う。</summary>
    public static void HandleDrop(DragEventArgs e, string? dest, IntPtr owner)
    {
        e.Handled = true;
        var files = GetFiles(e.Data);
        if (files == null || dest == null) return;

        var effect = Decide(e, dest, files);
        e.Effects = effect;
        if (effect == DragDropEffects.None) return;

        bool move = effect == DragDropEffects.Move;
        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            var result = FileOperationService.Transfer(files, dest, move, owner);
            if (result == FileOperationService.Result.Failed)
                MessageBox.Show("一部のファイルを処理できませんでした。", "KT_Explorer", MessageBoxButton.OK, MessageBoxImage.Warning);
        });
    }
}
