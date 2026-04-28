using System;
using System.Globalization;
using Microsoft.Maui.Controls;

namespace NovaStreamMobile.Converters
{
    public class NullToVisibilityConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            bool isNull = value == null;
            if (parameter?.ToString() == "Invert") isNull = !isNull;
            return isNull;
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
