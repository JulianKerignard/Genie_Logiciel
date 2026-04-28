using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using EasySave.UI.ViewModels;

namespace EasySave.UI.Converters;

public sealed class StateToColorConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        (UiJobState?)value switch
        {
            UiJobState.Running => new SolidColorBrush(Colors.Green),
            UiJobState.Paused  => new SolidColorBrush(Color.Parse("#FF6B35")),
            _                  => new SolidColorBrush(Color.Parse("#6B7280")),
        };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
