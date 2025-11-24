
using System;
using System.Windows;

namespace SmartMemeSearch.Wpf.Converters
{
    public class BoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type t, object p, string l)
            => (value is bool b && b) ? Visibility.Visible : Visibility.Collapsed;

        public object ConvertBack(object v, Type t, object p, string l)
            => throw new NotImplementedException();
    }

    public interface IValueConverter
    {
    }

    public class BoolToIndeterminateConverter : IValueConverter
    {
        public object Convert(object value, Type t, object p, string l)
            => value is bool b && b;

        public object ConvertBack(object v, Type t, object p, string l)
            => throw new NotImplementedException();
    }
}