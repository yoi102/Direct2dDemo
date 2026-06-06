using Avalonia.Data.Converters;
using Avalonia.Markup.Xaml;
using System;
using System.Globalization;

namespace Direct2dDemo.Avalonia.Converters;

public abstract class BaseValueConverter : MarkupExtension, IValueConverter
{
    public abstract object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture);

    public abstract object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture);

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        return this;
    }
}
