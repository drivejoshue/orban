using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Globalization;

namespace OrbanaDrive.Converters;

public sealed class IntNotZeroToBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is int i && i != 0;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => 0;
}
