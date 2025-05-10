using System;
using System.Globalization;
using System.Windows.Data;
using Harmony.Models;

namespace Harmony.Utils
{
    /// <summary>
    /// Converts PlaybackState enum to appropriate symbol
    /// </summary>
    public class PlaybackStateToSymbolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null)
                return "▶";

            PlaybackState state = (PlaybackState)value;

            return state switch
            {
                PlaybackState.Playing => "⏸", // Pause symbol when playing
                PlaybackState.Paused => "▶",  // Play symbol when paused
                PlaybackState.Stopped => "▶",  // Play symbol when stopped
                _ => "▶"
            };
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}