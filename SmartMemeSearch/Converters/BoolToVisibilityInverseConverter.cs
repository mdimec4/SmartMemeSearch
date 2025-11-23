using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using System;

namespace SmartMemeSearch.Converters
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
