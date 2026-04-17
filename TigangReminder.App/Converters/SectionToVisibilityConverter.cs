using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using TigangReminder_App.Models;

namespace TigangReminder_App.Converters;

public class SectionToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is not SectionKind current || parameter is not string raw)
        {
            return Visibility.Collapsed;
        }

        return Enum.TryParse<SectionKind>(raw, out var target) && current == target
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotSupportedException();
}
