using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Styling;

namespace SourceGit.Converters
{
    internal static class TokenChipPalette
    {
        private static readonly Color[] LightBorders =
        [
            Color.Parse("#1D4ED8"),
            Color.Parse("#0F766E"),
            Color.Parse("#B45309"),
            Color.Parse("#BE123C"),
            Color.Parse("#4338CA"),
            Color.Parse("#166534")
        ];

        private static readonly Color[] DarkBorders =
        [
            Color.Parse("#93C5FD"),
            Color.Parse("#5EEAD4"),
            Color.Parse("#FCD34D"),
            Color.Parse("#FDA4AF"),
            Color.Parse("#C4B5FD"),
            Color.Parse("#86EFAC")
        ];

        private static readonly Color[] LightTempBackground =
        [
            Color.Parse("#1A3B82F6"),
            Color.Parse("#1A14B8A6"),
            Color.Parse("#1AF59E0B"),
            Color.Parse("#1AF43F5E"),
            Color.Parse("#1A6366F1"),
            Color.Parse("#1A22C55E")
        ];

        private static readonly Color[] DarkTempBackground =
        [
            Color.Parse("#333B82F6"),
            Color.Parse("#3314B8A6"),
            Color.Parse("#33F59E0B"),
            Color.Parse("#33F43F5E"),
            Color.Parse("#336366F1"),
            Color.Parse("#3322C55E")
        ];

        private static readonly Color[] LightPersistentBackground =
        [
            Color.Parse("#553B82F6"),
            Color.Parse("#5514B8A6"),
            Color.Parse("#55F59E0B"),
            Color.Parse("#55F43F5E"),
            Color.Parse("#556366F1"),
            Color.Parse("#5522C55E")
        ];

        private static readonly Color[] DarkPersistentBackground =
        [
            Color.Parse("#704A90E2"),
            Color.Parse("#7040C9B6"),
            Color.Parse("#70D9A441"),
            Color.Parse("#70E05673"),
            Color.Parse("#70857AE5"),
            Color.Parse("#7050B878")
        ];

        private static bool IsOperatorToken(string token)
        {
            return token == "||" || token == "&&" || token == "|" || token == "&";
        }

        private static string GetPrefix(string token)
        {
            if (string.IsNullOrWhiteSpace(token) || IsOperatorToken(token))
                return string.Empty;

            var raw = token.StartsWith("-", StringComparison.Ordinal) ? token[1..] : token;
            var idx = raw.IndexOf(':');
            return idx <= 0 ? string.Empty : raw[..(idx + 1)].ToLowerInvariant();
        }

        private static int GetPaletteIndex(string token, int size)
        {
            var prefix = GetPrefix(token);
            if (string.IsNullOrEmpty(prefix) || size <= 0)
                return 0;

            var hash = Math.Abs(prefix.GetHashCode());
            return hash % size;
        }

        public static bool IsPersistent(string token, object persistentCollectionObj)
        {
            if (persistentCollectionObj is not System.Collections.IEnumerable enumerable || string.IsNullOrWhiteSpace(token))
                return false;

            foreach (var item in enumerable)
            {
                if (item is string s && string.Equals(s, token, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        public static IBrush GetBorderBrush(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
                return Brushes.Transparent;

            if (IsOperatorToken(token))
            {
                if (Application.Current.TryFindResource("Brush.FG2", out var fg2))
                    return fg2 as IBrush ?? Brushes.Gray;
                return Brushes.Gray;
            }

            if (token.StartsWith("-", StringComparison.Ordinal))
                return new SolidColorBrush(Color.Parse("#E11D48"));

            var dark = Application.Current?.ActualThemeVariant == ThemeVariant.Dark;
            var borders = dark ? DarkBorders : LightBorders;
            return new SolidColorBrush(borders[GetPaletteIndex(token, borders.Length)]);
        }

        public static IBrush GetBackgroundBrush(string token, bool persistent)
        {
            if (string.IsNullOrWhiteSpace(token))
                return Brushes.Transparent;

            if (IsOperatorToken(token))
            {
                if (Application.Current.TryFindResource("Brush.Border2", out var border))
                    return border as IBrush ?? Brushes.LightGray;
                return Brushes.LightGray;
            }

            if (token.StartsWith("-", StringComparison.Ordinal))
                return new SolidColorBrush(Color.Parse("#26E11D48"));

            var dark = Application.Current?.ActualThemeVariant == ThemeVariant.Dark;
            var palette = persistent
                ? (dark ? DarkPersistentBackground : LightPersistentBackground)
                : (dark ? DarkTempBackground : LightTempBackground);

            return new SolidColorBrush(palette[GetPaletteIndex(token, palette.Length)]);
        }
    }

    public class TokenToColorConverter : IMultiValueConverter
    {
        public object Convert(System.Collections.Generic.IList<object> values, Type targetType, object parameter, CultureInfo culture)
        {
            var token = values != null && values.Count > 0 ? values[0] as string : null;
            return TokenChipPalette.GetBorderBrush(token);
        }
    }

    public class TokenToBackgroundConverter : IMultiValueConverter
    {
        public object Convert(System.Collections.Generic.IList<object> values, Type targetType, object parameter, CultureInfo culture)
        {
            var token = values != null && values.Count > 0 ? values[0] as string : null;
            var persistentCollection = values != null && values.Count > 1 ? values[1] : null;
            var isPersistent = TokenChipPalette.IsPersistent(token, persistentCollection);
            return TokenChipPalette.GetBackgroundBrush(token, isPersistent);
        }
    }
}
