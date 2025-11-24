using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using System;

namespace SmartMemeSearch.Converters
{
    public class BoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type t, object p, string l)
            => (value is bool b && b) ? Visibility.Visible : Visibility.Collapsed;

        public object ConvertBack(object v, Type t, object p, string l)
            => throw new NotImplementedException();
    }

    public class BoolToIndeterminateConverter : IValueConverter
    {
        public object Convert(object value, Type t, object p, string l)
            => value is bool b && b;

        public object ConvertBack(object v, Type t, object p, string l)
            => throw new NotImplementedException();
    }
}