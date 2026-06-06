using System.Globalization;
using System.Windows.Data;

namespace Direct2dDemo.Converters;

public class InvertBooleanConverter : BaseValueConverter
{

    public override object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool b)
        {
            return !b;
        }
        else
            return Binding.DoNothing;
    }

    public override object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return Binding.DoNothing;
    }
}