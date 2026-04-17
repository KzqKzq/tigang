using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace TigangReminder_App.Converters;

public class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var invert = string.Equals(parameter as string, "Invert", StringComparison.OrdinalIgnoreCase);
        var isNull = value is null;
        return invert
            ? (isNull ? Visibility.Visible : Visibility.Collapsed)
            : (isNull ? Visibility.Collapsed : Visibility.Visible);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotSupportedException();
}
