using System;
using System.Globalization;

namespace Direct2dDemo.Avalonia.Converters;

public class InvertBooleanConverter : BaseValueConverter
{
    public override object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool b)
        {
            return !b;
        }
        else
            return null;
    }

    public override object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return null;
    }
}