using System.Windows.Controls;
using System.Windows.Input;

namespace KT_Explorer.Views;

/// <summary>右クリックメニューの、1 文字のショートカットキー(見出しの "削除(_D)" の D)のための補助。</summary>
public static class MenuKeys
{
    /// <summary>日本語入力がオンのままでも、キーが文字入力に取られないように、メニューでは IME を使わない。</summary>
    public static void DisableIme(ItemsControl menu)
    {
        InputMethod.SetIsInputMethodEnabled(menu, false);
        foreach (var item in menu.Items.OfType<MenuItem>()) DisableIme(item);
    }
}
