using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Data.Converters;

namespace SourceGit.Controls
{
    /// <summary>
    ///     计算 Token 在气泡组中的视觉位置。
    ///     注意：此 Converter 在 XAML 中必须额外绑定 SelectedTokens.Count，
    ///     因为 Avalonia 的 MultiBinding 不会自动监听集合内容的增减。
    /// </summary>
    public class TokenBubblePositionConverter : IMultiValueConverter
    {
        public object Convert(IList<object> values, Type targetType, object parameter, CultureInfo culture)
        {
            // 鲁棒性检查：确保传入了 Token 和 完整的集合
            if (values == null || values.Count < 2)
                return "Normal";
            if (values[0] == null || values[0] == AvaloniaProperty.UnsetValue)
                return "Normal";
            if (values[1] == null || values[1] == AvaloniaProperty.UnsetValue)
                return "Normal";

            if (values[0] is not string token || values[1] is not IEnumerable<string> allTokens)
                return "Normal";

            var tokens = allTokens.ToList();
            if (tokens.Count == 0)
                return "Normal";

            var index = tokens.IndexOf(token);
            if (index < 0)
                return "Normal";

            static bool IsOperatorToken(string t)
            {
                return t == "||" || t == "&&" || t == "|" || t == "&";
            }

            // 辅助方法：提取前缀（剥离负号）
            string GetPrefix(string t)
            {
                if (string.IsNullOrEmpty(t))
                    return null;
                if (IsOperatorToken(t))
                    return null;
                var s = t.StartsWith("-") ? t.Substring(1) : t;
                var colonIdx = s.IndexOf(':');
                return colonIdx >= 0 ? s.Substring(0, colonIdx + 1) : s;
            }

            var currentPrefix = GetPrefix(token);

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

            // 情况 B：当前是一个普通的过滤 Token
            // 只有当左右相邻的是“同类”或者“属于该组的逻辑符”时，才视为在组内
            bool IsSameGroup(int otherIdx)
            {
                if (otherIdx < 0 || otherIdx >= tokens.Count)
                    return false;
                var other = tokens[otherIdx];
                var otherPrefix = GetPrefix(other);

                if (otherPrefix != null)
                    return otherPrefix == currentPrefix;

                // 如果邻居是逻辑符，看逻辑符的另一边是不是也是同类
                if (IsOperatorToken(other))
                {
                    int neighborOfOp = (otherIdx < index) ? otherIdx - 1 : otherIdx + 1;
                    if (neighborOfOp >= 0 && neighborOfOp < tokens.Count)
                    {
                        return GetPrefix(tokens[neighborOfOp]) == currentPrefix;
                    }
                }
                return false;
            }

            var prevSame = IsSameGroup(index - 1);
            var nextSame = IsSameGroup(index + 1);

            if (prevSame && nextSame)
                return "Middle";
            if (prevSame)
                return "End";
            if (nextSame)
                return "Start";

            return "Normal";
        }
    }
}
