using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace KT_Explorer.Views;

/// <summary>
/// 複数のバインドのうち、最初に値(ブラシ)があるものを使う。一覧の背景は、ペインの「参照元のグループの色」があればそれ、
/// 無ければ「表示中のグループの色」。
/// </summary>
public sealed class FirstBrushConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) =>
        values.OfType<Brush>().FirstOrDefault() ?? DependencyProperty.UnsetValue;

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
