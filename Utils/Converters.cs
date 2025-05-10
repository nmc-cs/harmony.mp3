using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Input;

namespace Harmony.Utils
{
    /// <summary>
    /// Converts boolean to one of two strings (e.g., play/pause symbols)
    /// </summary>
    public class BoolToSymbolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool boolValue = (bool)value;
            string[] options = ((string)parameter).Split('|');
            return boolValue ? options[1] : options[0];
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Selects between two commands based on a boolean value
    /// </summary>
    public class BoolToCommandConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null)
                return null;

            bool boolValue = (bool)value;
            string[] commandNames = ((string)parameter).Split('|');
            string commandName = boolValue ? commandNames[0] : commandNames[1];

            // Find the command in the data context
            if (System.Windows.Application.Current.MainWindow?.DataContext is ViewModels.MainViewModel viewModel)
            {
                var propertyInfo = viewModel.GetType().GetProperty(commandName);
                if (propertyInfo != null)
                {
                    return propertyInfo.GetValue(viewModel) as ICommand;
                }
            }

            return null;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}