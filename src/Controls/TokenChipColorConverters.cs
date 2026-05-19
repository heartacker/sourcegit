using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Styling;

namespace SourceGit.Controls
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
            Color.Parse("#166534"),
            Color.Parse("#0E7490"),
            Color.Parse("#7C3AED"),
            Color.Parse("#A16207"),
            Color.Parse("#15803D"),
            Color.Parse("#C2410C"),
            Color.Parse("#334155")
        ];

        private static readonly Color[] DarkBorders =
        [
            Color.Parse("#93C5FD"),
            Color.Parse("#5EEAD4"),
            Color.Parse("#FCD34D"),
            Color.Parse("#FDA4AF"),
            Color.Parse("#C4B5FD"),
            Color.Parse("#86EFAC"),
            Color.Parse("#67E8F9"),
            Color.Parse("#DDD6FE"),
            Color.Parse("#FDE68A"),
            Color.Parse("#BBF7D0"),
            Color.Parse("#FDBA74"),
            Color.Parse("#CBD5E1")
        ];

        private static readonly Color[] LightTempBackground =
        [
            Color.Parse("#143B82F6"),
            Color.Parse("#1414B8A6"),
            Color.Parse("#14F59E0B"),
            Color.Parse("#14F43F5E"),
            Color.Parse("#146366F1"),
            Color.Parse("#1422C55E"),
            Color.Parse("#1406B6D4"),
            Color.Parse("#147C3AED"),
            Color.Parse("#14CA8A04"),
            Color.Parse("#1416A34A"),
            Color.Parse("#14EA580C"),
            Color.Parse("#14334155")
        ];

        private static readonly Color[] DarkTempBackground =
        [
            Color.Parse("#283B82F6"),
            Color.Parse("#2814B8A6"),
            Color.Parse("#28F59E0B"),
            Color.Parse("#28F43F5E"),
            Color.Parse("#286366F1"),
            Color.Parse("#2822C55E"),
            Color.Parse("#2806B6D4"),
            Color.Parse("#287C3AED"),
            Color.Parse("#28CA8A04"),
            Color.Parse("#2816A34A"),
            Color.Parse("#28EA580C"),
            Color.Parse("#28334155")
        ];

        private static readonly Color[] LightPersistentBackground =
        [
            Color.Parse("#C03B82F6"),
            Color.Parse("#C014B8A6"),
            Color.Parse("#C0F59E0B"),
            Color.Parse("#C0F43F5E"),
            Color.Parse("#C06366F1"),
            Color.Parse("#C022C55E"),
            Color.Parse("#C006B6D4"),
            Color.Parse("#C07C3AED"),
            Color.Parse("#C0CA8A04"),
            Color.Parse("#C016A34A"),
            Color.Parse("#C0EA580C"),
            Color.Parse("#C0334155")
        ];

        private static readonly Color[] DarkPersistentBackground =
        [
            Color.Parse("#A04A90E2"),
            Color.Parse("#A040C9B6"),
            Color.Parse("#A0D9A441"),
            Color.Parse("#A0E05673"),
            Color.Parse("#A0857AE5"),
            Color.Parse("#A050B878"),
            Color.Parse("#A03BB8D6"),
            Color.Parse("#A09672F4"),
            Color.Parse("#A0D7B45A"),
            Color.Parse("#A05DB071"),
            Color.Parse("#A0DE8A67"),
            Color.Parse("#A0647B93")
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
