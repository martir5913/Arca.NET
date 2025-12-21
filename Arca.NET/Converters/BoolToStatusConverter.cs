using System.Globalization;
using System.Windows.Data;

namespace Arca.NET;

public class BoolToStatusConverter : IValueConverter
{
    // Convierte un booleano de Success a texto con color para el log de auditoría.
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool success)
        {
            return success ? "✅" : "❌";
        }
        return "?";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
