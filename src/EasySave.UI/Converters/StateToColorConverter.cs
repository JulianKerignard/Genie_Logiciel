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
            UiJobState.Running => new SolidColorBrush(Color.Parse("#3B82F6")),    // blue
            UiJobState.Paused => new SolidColorBrush(Color.Parse("#FF6B35")),     // orange
            UiJobState.Completed => new SolidColorBrush(Color.Parse("#22C55E")),  // green
            _ => new SolidColorBrush(Color.Parse("#6B7280")),                     // gray (idle)
        };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
