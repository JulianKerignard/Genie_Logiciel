using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using EasySave.RemoteConsole.Infrastructure;

namespace EasySave.RemoteConsole.Converters;

public sealed class ConnectionStateToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value switch
        {
            ConnectionState.Connected    => new SolidColorBrush(Color.Parse("#16A34A")),
            ConnectionState.Connecting   => new SolidColorBrush(Color.Parse("#D97706")),
            ConnectionState.Error        => new SolidColorBrush(Color.Parse("#DC2626")),
            _                            => new SolidColorBrush(Color.Parse("#374151")),
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
