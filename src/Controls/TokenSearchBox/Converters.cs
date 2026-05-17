using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Styling;

namespace SourceGit.Controls
{
    #region CHIP_COLOR
    public static class TokenChipPalette
    {
        private static readonly Color[] LightBorders =
        {
            Color.Parse("#3B82F6"), Color.Parse("#10B981"), Color.Parse("#F59E0B"),
            Color.Parse("#8B5CF6"), Color.Parse("#EC4899"), Color.Parse("#06B6D4")
        };

        private static readonly Color[] DarkBorders =
        {
            Color.Parse("#60A5FA"), Color.Parse("#34D399"), Color.Parse("#FBBF24"),
            Color.Parse("#A78BFA"), Color.Parse("#F472B6"), Color.Parse("#22D3EE")
        };

        private static readonly Color[] LightTempBackground =
        {
            Color.Parse("#203B82F6"), Color.Parse("#2010B981"), Color.Parse("#20F59E0B"),
            Color.Parse("#208B5CF6"), Color.Parse("#20EC4899"), Color.Parse("#2006B6D4")
        };

        private static readonly Color[] DarkTempBackground =
        {
            Color.Parse("#2060A5FA"), Color.Parse("#2034D399"), Color.Parse("#20FBBF24"),
            Color.Parse("#20A78BFA"), Color.Parse("#20F472B6"), Color.Parse("#2022D3EE")
        };

        private static readonly Color[] LightPersistentBackground =
        {
            Color.Parse("#403B82F6"), Color.Parse("#4010B981"), Color.Parse("#40F59E0B"),
            Color.Parse("#408B5CF6"), Color.Parse("#40EC4899"), Color.Parse("#4006B6D4")
        };

        private static readonly Color[] DarkPersistentBackground =
        {
            Color.Parse("#4060A5FA"), Color.Parse("#4034D399"), Color.Parse("#40FBBF24"),
            Color.Parse("#40A78BFA"), Color.Parse("#40F472B6"), Color.Parse("#4022D3EE")
        };

        public static int GetPaletteIndex(string token, int count)
        {
            if (string.IsNullOrEmpty(token))
                return 0;
            var s = token.StartsWith("-") ? token.Substring(1) : token;
            var colonIdx = s.IndexOf(':');
            var prefix = colonIdx >= 0 ? s.Substring(0, colonIdx + 1).ToLowerInvariant() : s.ToLowerInvariant();
            var hash = 0;
            foreach (var c in prefix)
                hash = (hash * 31) + c;
            return Math.Abs(hash) % count;
        }

        public static bool IsOperatorToken(string token)
        {
            return token == "||" || token == "&&" || token == "|" || token == "&" || token == "(" || token == ")";
        }

        public static bool IsPersistent(string token, object persistentCollection)
        {
            if (string.IsNullOrEmpty(token))
                return false;
            if (persistentCollection is IEnumerable<TokenInstance> collection)
            {
                return collection.Any(x => x.Raw.Equals(token, StringComparison.OrdinalIgnoreCase));
            }
            if (persistentCollection is IEnumerable<string> strings)
            {
                return strings.Any(x => x.Equals(token, StringComparison.OrdinalIgnoreCase));
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
            var palette = persistent ? (dark ? DarkPersistentBackground : LightPersistentBackground)
                                     : (dark ? DarkTempBackground : LightTempBackground);

            return new SolidColorBrush(palette[GetPaletteIndex(token, palette.Length)]);
        }
    }

    public class TokenToColorConverter : IMultiValueConverter
    {
        public object Convert(IList<object> values, Type targetType, object parameter, CultureInfo culture)
        {
            var token = values != null && values.Count > 0 ? values[0] as string : null;
            return TokenChipPalette.GetBorderBrush(token);
        }
    }

    public class TokenToBackgroundConverter : IMultiValueConverter
    {
        public object Convert(IList<object> values, Type targetType, object parameter, CultureInfo culture)
        {
            var token = values != null && values.Count > 0 ? values[0] as string : null;
            var persistentCollection = values != null && values.Count > 1 ? values[1] : null;
            var isPersistent = TokenChipPalette.IsPersistent(token, persistentCollection);
            return TokenChipPalette.GetBackgroundBrush(token, isPersistent);
        }
    }
    #endregion

    #region BUBBLE_POSITION
    /// <summary>
    ///     计算 Token 在气泡组中的视觉位置。
    ///     注意：此 Converter 在 XAML 中必须额外绑定 SelectedTokens.Count，
    ///     因为 Avalonia 的 MultiBinding 不会自动监听集合内容的增减。
    /// </summary>
    public class TokenBubblePositionConverter : IMultiValueConverter
    {
        public object Convert(IList<object> values, Type targetType, object parameter, CultureInfo culture)
        {
            // 鲁棒性检查
            if (values == null || values.Count < 2)
                return "Normal";
            if (values[0] == null || values[0] == AvaloniaProperty.UnsetValue)
                return "Normal";
            if (values[1] == null || values[1] == AvaloniaProperty.UnsetValue)
                return "Normal";

            if (values[0] is not string raw || values[1] is not IEnumerable<TokenInstance> allTokens)
                return "Normal";

            var tokens = allTokens.ToList();
            if (tokens.Count == 0)
                return "Normal";

            var index = tokens.FindIndex(x => x.Raw == raw);
            if (index < 0)
                return "Normal";

            var current = tokens[index];

            static bool IsOperatorToken(TokenInstance t)
            {
                return t.IsOperator || t.Raw == "||" || t.Raw == "&&" || t.Raw == "|" || t.Raw == "&" || t.Raw == "(" ||
                       t.Raw == ")";
            }

            // 辅助方法：提取前缀
            static string GetPrefix(TokenInstance t)
            {
                if (t == null)
                    return null;
                if (IsOperatorToken(t))
                    return null;
                if (t.Provider != null)
                    return t.Provider.Prefix;

                var s = t.Raw.StartsWith("-") ? t.Raw.Substring(1) : t.Raw;
                var colonIdx = s.IndexOf(':');
                return colonIdx >= 0 ? s.Substring(0, colonIdx + 1) : s;
            }

            var currentPrefix = GetPrefix(current);

            // 情况 A：当前项是逻辑运算符
            if (currentPrefix == null)
            {
                // 如果它夹在两个前缀相同的 Token 之间，它就属于该气泡组的“内部线”
                if (index > 0 && index < tokens.Count - 1)
                {
                    var p = GetPrefix(tokens[index - 1]);
                    var n = GetPrefix(tokens[index + 1]);
                    if (p != null && p == n)
                        return "Operator";
                }

                return "Normal";
            }

            // 情况 B：当前项是数据 Token
            var hasPrevSame = index > 0 && GetPrefix(tokens[index - 1]) == currentPrefix;
            var hasNextSame = index < tokens.Count - 1 && GetPrefix(tokens[index + 1]) == currentPrefix;

            // 补充逻辑：检查中间是否有操作符连接同一前缀
            if (!hasPrevSame && index > 1 && IsOperatorToken(tokens[index - 1]))
            {
                if (GetPrefix(tokens[index - 2]) == currentPrefix)
                    hasPrevSame = true;
            }

            if (!hasNextSame && index < tokens.Count - 2 && IsOperatorToken(tokens[index + 1]))
            {
                if (GetPrefix(tokens[index + 2]) == currentPrefix)
                    hasNextSame = true;
            }

            if (hasPrevSame && hasNextSame)
                return "Middle";
            if (hasPrevSame)
                return "End";
            if (hasNextSame)
                return "Start";

            return "Normal";
        }
    }

    /// <summary>
    ///     当有已选择的 Token 时，返回空字符串隐藏占位符；否则返回原始占位文字。
    /// </summary>
    public class PlaceholderTextConverter : IMultiValueConverter
    {
        public object Convert(IList<object> values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values == null || values.Count < 2)
                return string.Empty;

            var baseText = values[0] as string ?? string.Empty;
            if (values[1] is int count && count > 0)
                return string.Empty;

            return baseText;
        }
    }
    #endregion

    #region UTILS
    public class BoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool b)
                return b;
            return true;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public static class LogicConverters
    {
        public static readonly IValueConverter IsNullOrEmpty = new FuncValueConverter<string, bool>(string.IsNullOrEmpty);
        public static readonly IValueConverter IsZero = new FuncValueConverter<int, bool>(v => v == 0);
        public static readonly IMultiValueConverter And = new FuncMultiValueConverter<bool, bool>(values => values.All(x => x));
    }
    #endregion
}
