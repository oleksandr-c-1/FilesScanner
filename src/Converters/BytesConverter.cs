using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace FilesScanner.Converters;

internal class BytesConverter : IValueConverter {
    readonly string[] _suffixes = { "B", "KB", "MB", "GB", "TB" };
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
        var byteCount = value is long l ? l : 0;
        if (byteCount == 0) {
            return $"0 {_suffixes[0]}";
        }
        var bytes = Math.Abs(byteCount);
        var place = System.Convert.ToInt32(Math.Floor(Math.Log(bytes, 1024)));
        var num = Math.Round(bytes / Math.Pow(1024, place), 1);
        return $"{(Math.Sign(byteCount) * num).ToString(CultureInfo.InvariantCulture)} {_suffixes[place]}";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) {
        return DependencyProperty.UnsetValue;
    }
}