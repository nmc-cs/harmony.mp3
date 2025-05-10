using System;
using System.Globalization;
using System.Windows.Data;
using Harmony.Models;

namespace Harmony.Utils
{
    /// <summary>
    /// Converts RepeatMode enum to appropriate symbol
    /// </summary>
    public class RepeatModeToSymbolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null)
                return "🔁";

            RepeatMode mode = (RepeatMode)value;

            return mode switch
            {
                RepeatMode.None => "↩️",   // No repeat
                RepeatMode.One => "🔂",     // Repeat one
                RepeatMode.All => "🔁",     // Repeat all
                _ => "🔁"
            };
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}