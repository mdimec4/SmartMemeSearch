
using System;
using System.Windows;

namespace SmartMemeSearch.Wpf.Converters
{
    public class BoolToVisibilityInverseConverter : IValueConverter
    {
        // true  -> Collapsed
        // false -> Visible
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            bool b = value is bool bv && bv;
            return b ? Visibility.Collapsed : Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
}
