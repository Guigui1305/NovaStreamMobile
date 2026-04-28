using System;
using System.Globalization;
using Microsoft.Maui.Controls;

namespace NovaStreamMobile.Converters
{
    public class ProgressConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is double d)
            {
                return d / 100.0; // MAUI ProgressBar uses 0.0 to 1.0
            }
            return 0.0;
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
