using System.Globalization;
using System.Windows.Data;

namespace SmartMemeSearch.Wpf.Converters
{
    public class TileWidthConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double totalWidth)
            {
                double margin = 37;

                return totalWidth - margin;
            }

            return 300; // safe fallback
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }
}