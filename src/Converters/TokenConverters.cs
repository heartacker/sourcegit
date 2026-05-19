using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace SourceGit.Converters
{
    public class TokenToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string token)
            {
                if (token == "|" || token == "&")
                {
                    if (Application.Current.TryFindResource("Brush.FG2", out var fg2))
                        return fg2;
                    return Brushes.Gray;
                }
                if (token.StartsWith("-"))
                {
                    return Brushes.Red;
                }
                if (token.StartsWith("ui:"))
                {
                    return Brushes.Orange;
                }
                if (Application.Current.TryFindResource("Brush.Accent", out var accent))
                    return accent;
                return Brushes.DodgerBlue;
            }
            return Brushes.Transparent;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class TokenToBackgroundConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string token)
            {
                if (token == "|" || token == "&")
                {
                    if (Application.Current.TryFindResource("Brush.Border2", out var border))
                        return border;
                    return Brushes.LightGray;
                }
                if (token.StartsWith("-"))
                {
                    return Color.Parse("#20FF0000");
                }
                if (token.StartsWith("ui:"))
                {
                    return Color.Parse("#20FFA500");
                }
                if (Application.Current.TryFindResource("Brush.AccentHovered", out var accentHover))
                    return accentHover;
            }
            return Brushes.Transparent;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
