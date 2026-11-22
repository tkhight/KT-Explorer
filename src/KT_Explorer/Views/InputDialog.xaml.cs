using System.Windows;
using System.Windows.Input;

namespace KT_Explorer.Views;

/// <summary>1行入力ダイアログ(グループ名の入力用)。</summary>
public partial class InputDialog : Window
{
    private InputDialog(string title, string prompt, string initial, bool selectBaseNameOnly)
    {
        InitializeComponent();
        Title = title;
        PromptText.Text = prompt;
        InputBox.Text = initial;
        Loaded += (_, _) =>
        {
            InputBox.Focus();
            // ファイル名の変更では、拡張子を除いた部分だけを選択しておく
            int dot = initial.LastIndexOf('.');
            if (selectBaseNameOnly && dot > 0) InputBox.Select(0, dot);
            else InputBox.SelectAll();
        };
    }

    private void Ok_Click(object sender, RoutedEventArgs e) => DialogResult = true;

    private void InputBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) DialogResult = true;
    }

    /// <summary>入力された文字列を返す。キャンセルや空入力のときは null。</summary>
    public static string? Ask(Window owner, string title, string prompt, string initial = "", bool selectBaseNameOnly = false)
    {
        var dlg = new InputDialog(title, prompt, initial, selectBaseNameOnly) { Owner = owner };
        if (dlg.ShowDialog() != true) return null;
        string text = dlg.InputBox.Text.Trim();
        return text.Length == 0 ? null : text;
    }
}
