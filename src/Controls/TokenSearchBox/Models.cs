using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SourceGit.Controls
{
    #region INTERFACES
    /// <summary>
    ///     Provides suggestions for a specific token prefix.
    /// </summary>
    public interface ITokenSuggestionProvider
    {
        /// <summary>
        ///     The prefix that triggers this provider (e.g., "author:", "b:").
        /// </summary>
        string Prefix { get; }

        /// <summary>
        ///     Optional alternative prefixes that also trigger this provider (e.g., "a:" for "author:").
        /// </summary>
        string[] FullPrefix { get; }

        /// <summary>
        ///     An optional description for this provider (e.g., "Filter by author").
        /// </summary>
        string Description { get; }

        /// <summary>
        ///     An optional icon key for this provider displayed in the suggestions list.
        /// </summary>
        object Icon { get; }

        /// <summary>
        ///     An optional group for categorizing this provider in the suggestions list.
        /// </summary>
        TokenSuggestionGroup Group { get; }

        /// <summary>
        ///     The logic mode for this provider (e.g., SingleReplace, AutoOr).
        /// </summary>
        TokenLogicMode LogicMode { get; }

        /// <summary>
        ///     Indicates whether tokens from this provider should be persisted.
        /// </summary>
        bool IsPersistent { get; }

        /// <summary>
        ///     Priority for ordering and cache computation. Lower value = higher priority.
        /// </summary>
        int Priority { get; }

        /// <summary>
        ///     [双语注释 / Bilingual Comment]
        ///     指示过滤与去重时是否区分大小写。默认不区分。
        ///     Indicates whether filtering and deduplication should be case-sensitive. Defaults to false.
        /// </summary>
        bool CaseSensitive { get; }

        /// <summary>
        ///     Returns a list of suggestions based on the user's current input after the prefix.
        /// </summary>
        /// <param name="pattern">The text user typed after the prefix.</param>
        /// <param name="cancellationToken">Cancellation token for aborting the request.</param>
        /// <returns>A list of suggested values.</returns>
        Task<IEnumerable<TokenSuggestion>> GetSuggestionsAsync(string pattern, CancellationToken cancellationToken);
    }

    /// <summary>
    /// 增强型 Token 提示器，支持值转换 and 自定义编辑器渲染约束。
    /// </summary>
    public interface IAdvancedTokenProvider : ITokenSuggestionProvider
    {
        /// <summary>
        /// 值转换器。如果为空，则使用默认的纯文本模式。
        /// </summary>
        ITokenValueConverter ValueConverter { get; }

        /// <summary>
        /// 指定 UI 应该渲染成什么编辑器类型。
        /// </summary>
        TokenEditorType EditorType { get; }
    }

    /// <summary>
    /// 提供将用户输入的文本和业务所需的逻辑值进行双向转换的能力。
    /// </summary>
    public interface ITokenValueConverter
    {
        /// <summary>
        /// 将解析出的文本 (如 "20260512") 转为规范格式或业务需要的对象 (如 DateTime 或规范化的 SHA)。
        /// </summary>
        /// <param name="text">用户输入的原始文本或补全选中的 Value。</param>
        /// <returns>转换后的对象或规范化的字符串。</returns>
        object ToValue(string text);

        /// <summary>
        /// 将业务值反向转换为在 UI TokenChip 上显示的文本。
        /// </summary>
        /// <param name="value">由 ToValue 转换得到的业务对象。</param>
        /// <returns>展示给用户的字符串。</returns>
        string ToDisplay(object value);
    }
    #endregion

    #region ENUMS
    /// <summary>
    ///     Defines the logic mode for how tokens from a provider should be combined.
    /// </summary>
    public enum TokenLogicMode
    {
        /// <summary>
        ///     Default for most providers,
        ///     automatically combines multiple tokens with OR logic.
        ///     like "author:Alice author:Bob" will match commits by Alice or Bob.
        /// </summary>
        AutoOr,

        /// <summary>
        ///     Automatically combines multiple tokens with AND logic.
        ///     like "file:src/ file:docs/" will match commits that touch both src/ and docs/.
        /// </summary>
        AutoAnd,

        /// <summary>
        /// like sort:Commit-Date and   Topologically, the second token will replace the first one instead of being
        /// combined.
        /// </summary>
        SingleReplace,
    }

    /// <summary>
    ///     Represents a suggestion item for the token search box.
    /// </summary>
    public enum TokenSuggestionActionType
    {
        Insert,
        Execute,
    }

    /// <summary>
    /// 定义 Token 在 UI 上推荐使用的编辑器类型。
    /// </summary>
    public enum TokenEditorType
    {
        /// <summary>
        /// 默认文本框输入。
        /// </summary>
        Text,

        /// <summary>
        /// 日期选择器。
        /// </summary>
        Date,

        /// <summary>
        /// 开关/切换器（如 true/false/toggle）。
        /// </summary>
        Toggle,

        /// <summary>
        /// 列表选择。
        /// </summary>
        List
    }
    #endregion

    #region MODELS
    public class TokenSuggestion
    {
        /// <summary>
        ///     显示名称。如果为空，则默认使用 <see cref="Value"/>。
        ///     用于在建议列表中展示给用户看（规则1：展示优先使用 Name）。
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        ///     实际逻辑值。如果为空，则默认使用 <see cref="Name"/>。
        ///     用于实际的搜索或补全操作（规则2：补全优先使用 Value）。
        /// </summary>
        public string Value { get; set; }

        public string Description { get; set; }
        public object Icon { get; set; }
        public bool IsSlashCommand { get; set; }
        public string SlashCommandName { get; set; }
        public string SlashCommandArgument { get; set; }
        public bool CanExecuteDirectly { get; set; }
        public TokenSuggestionActionType ActionType { get; set; } = TokenSuggestionActionType.Insert;

        /// <summary>
        ///     获取用于 UI 展示的文本（规则1）。
        /// </summary>
        public string DisplayName => string.IsNullOrWhiteSpace(Name) ? Value : Name;

        /// <summary>
        ///     获取用于补全上屏或执行的实际值（规则2）。
        /// </summary>
        public string InsertValue => string.IsNullOrWhiteSpace(Value) ? Name : Value;

        public string ActionTooltip => ActionType == TokenSuggestionActionType.Execute ? "执行" : "上屏";

        public string ActionIcon =>
            ActionType == TokenSuggestionActionType.Execute
                ? "M6.5,1 L2,6.5 H5.5 L4.5,11 L9,5.5 H5.5 Z"
                : "M8.5,1.5 A1,1 0 0,1 10,3 L9,4 L7,2 M6.3,2.7 L1,8 L1,10 H3 L8.3,4.7 Z";
    }

    /// <summary>
    ///     Represents a group for token suggestions, facilitating localization and structured display.
    /// </summary>
    public class TokenSuggestionGroup
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public int Priority { get; set; }

        public TokenSuggestionGroup(string id, string name, int priority = 0)
        {
            Id = id;
            Name = name;
            Priority = priority;
        }

        public override bool Equals(object obj) => obj is TokenSuggestionGroup other && Id == other.Id;
        public override int GetHashCode() => Id?.GetHashCode() ?? 0;
        public override string ToString() => Id;
    }

    /// <summary>
    ///     Represents a group header in the suggestions list.
    /// </summary>
    public class TokenSuggestionHeader
    {
        public string Name { get; set; }
    }

    /// <summary>
    /// 定义斜杠命令（Slash Command）的单个参数定义。
    /// </summary>
    public class TokenArgumentDefinition
    {
        /// <summary>
        /// 参数名称。
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// 参数描述。
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// 是否为必填参数。
        /// </summary>
        public bool IsRequired { get; set; } = true;

        /// <summary>
        /// 用于提供该参数建议的提示器。
        /// </summary>
        public ITokenSuggestionProvider SuggestionProvider { get; set; }

        /// <summary>
        /// 允许根据已输入的参数动态生成提示器的委托。
        /// </summary>
        public Func<TokenSlashSuggestionContext, ITokenSuggestionProvider> DynamicProvider { get; set; }
    }

    /// <summary>
    /// 表示一个已选中的结构化 Token 实例。
    /// </summary>
    public class TokenInstance
    {
        /// <summary>
        /// 原始文本（如 "a:Acker"）。用于解析和重建输入流。
        /// </summary>
        public string Raw { get; set; }

        /// <summary>
        /// 关联的提示器。
        /// </summary>
        public ITokenSuggestionProvider Provider { get; set; }

        /// <summary>
        /// 转换后的业务值（如 Author 对象或 SHA 字符串）。
        /// </summary>
        public object Value { get; set; }

        /// <summary>
        /// 最终在芯片上显示的文本。
        /// </summary>
        public string DisplayText { get; set; }

        /// <summary>
        /// 是否为取反模式。
        /// </summary>
        public bool IsNegated { get; set; }

        /// <summary>
        /// 是否为操作符（||, &&, (, )）。
        /// </summary>
        public bool IsOperator { get; set; }

        public override string ToString() => Raw;
    }
    #endregion

    #region PROVIDERS
    /// <summary>
    ///     A simple implementation of ITokenSuggestionProvider that provides static suggestions.
    /// </summary>
    public class StaticTokenSuggestionProvider : IAdvancedTokenProvider
    {
        public string Prefix { get; }
        public string[] FullPrefix { get; }
        public string Description { get; }
        public object Icon { get; }
        public TokenSuggestionGroup Group { get; }
        public TokenLogicMode LogicMode { get; }
        public bool IsPersistent { get; }
        public int Priority { get; }
        public ITokenValueConverter ValueConverter { get; set; }
        public TokenEditorType EditorType { get; set; }
        public bool CaseSensitive { get; set; } = false;

        private readonly Func<string, CancellationToken, Task<IEnumerable<TokenSuggestion>>> _suggester;
        private readonly IEnumerable<TokenSuggestion> _staticOptions;

        public StaticTokenSuggestionProvider(
            string prefix, string description, TokenSuggestionGroup group = null,
            Func<string, CancellationToken, Task<IEnumerable<TokenSuggestion>>> suggester = null,
            TokenLogicMode logicMode = TokenLogicMode.AutoOr, string[] alias = null, object icon = null,
            bool isPersistent = false, int priority = 0, ITokenValueConverter valueConverter = null,
            TokenEditorType editorType = TokenEditorType.Text, bool caseSensitive = false)
        {
            Prefix = prefix;
            FullPrefix = alias;
            Description = description;
            Icon = icon;
            Group = group;
            _suggester = suggester;
            LogicMode = logicMode;
            IsPersistent = isPersistent;
            Priority = priority;
            ValueConverter = valueConverter;
            EditorType = editorType;
            CaseSensitive = caseSensitive;
        }

        public StaticTokenSuggestionProvider(string prefix, string description, TokenSuggestionGroup group,
                                             IEnumerable<string> staticOptions,
                                             TokenLogicMode logicMode = TokenLogicMode.AutoOr, string[] alias = null,
                                             object icon = null, bool isPersistent = false, int priority = 0,
                                             ITokenValueConverter valueConverter = null,
                                             TokenEditorType editorType = TokenEditorType.Text, bool caseSensitive = false)
        {
            Prefix = prefix;
            FullPrefix = alias;
            Description = description;
            Icon = icon;
            Group = group;
            _staticOptions = staticOptions.Select(x => new TokenSuggestion { Name = x }).ToList();
            LogicMode = logicMode;
            IsPersistent = isPersistent;
            Priority = priority;
            ValueConverter = valueConverter;
            EditorType = editorType;
            CaseSensitive = caseSensitive;
        }

        public Task<IEnumerable<TokenSuggestion>> GetSuggestionsAsync(string pattern, CancellationToken cancellationToken)
        {
            if (_suggester != null)
            {
                return _suggester(pattern, cancellationToken);
            }

            if (_staticOptions != null)
            {
                var filtered = _staticOptions.Where(
                    x => string.IsNullOrEmpty(pattern) ||
                         (x.Name != null && x.Name.Contains(pattern, StringComparison.OrdinalIgnoreCase)) ||
                         (x.Value != null && x.Value.Contains(pattern, StringComparison.OrdinalIgnoreCase)));
                return Task.FromResult(filtered);
            }

            return Task.FromResult(Enumerable.Empty<TokenSuggestion>());
        }
    }

    /// <summary>
    /// 一个简单的委托提示器，用于快速包装逻辑。
    /// </summary>
    public class DelegateTokenSuggestionProvider : ITokenSuggestionProvider
    {
        private readonly Func<string, CancellationToken, Task<IEnumerable<TokenSuggestion>>> _suggester;

        public string Prefix => string.Empty;
        public string[] FullPrefix => null;
        public string Description => string.Empty;
        public object Icon => string.Empty;
        public TokenSuggestionGroup Group => null;
        public TokenLogicMode LogicMode => TokenLogicMode.AutoOr;
        public bool IsPersistent => false;
        public int Priority => 0;
        public bool CaseSensitive => false;

        public DelegateTokenSuggestionProvider(
            Func<string, CancellationToken, Task<IEnumerable<TokenSuggestion>>> suggester)
        {
            _suggester = suggester;
        }

        public Task<IEnumerable<TokenSuggestion>> GetSuggestionsAsync(string pattern, CancellationToken cancellationToken)
        {
            return _suggester(pattern, cancellationToken);
        }
    }
    #endregion

    #region HELPERS
    public class DateValueConverter : ITokenValueConverter
    {
        public object ToValue(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return null;

            // Handle pure numbers like "20260512"
            if (text.Length == 8 && int.TryParse(text, out _))
            {
                if (DateTimeOffset.TryParseExact(text, "yyyyMMdd", null, System.Globalization.DateTimeStyles.None,
                                                 out var dto))
                    return dto;
            }

            // Normal parse
            if (DateTimeOffset.TryParse(text, out var dt))
            {
                return dt;
            }

            return null; // or throw/keep original text
        }

        public string ToDisplay(object value)
        {
            if (value is DateTimeOffset dto)
            {
                return dto.ToString("yyyy-MM-dd");
            }
            return value?.ToString() ?? string.Empty;
        }
    }

    public static class TokenValueHelper
    {
        public static string Unescape(string val)
        {
            if (string.IsNullOrEmpty(val))
                return val;

            if (val.Length >= 2 && val.StartsWith("\"") && val.EndsWith("\""))
            {
                val = val.Substring(1, val.Length - 2);
                val = val.Replace("\\\"", "\"").Replace("\\\\", "\\");
            }
            return val;
        }

        public static string Escape(string val)
        {
            if (string.IsNullOrEmpty(val))
                return val;

            if (val.EndsWith(':') && val.IndexOf(':') == val.Length - 1)
            {
                return val;
            }

            if (val.Contains(' ') || val.Contains('"') || val.Contains('\\') ||
                val.Contains('|') || val.Contains('&') || val.Contains(':') ||
                val.Contains('(') || val.Contains(')'))
            {
                var escaped = val.Replace("\\", "\\\\").Replace("\"", "\\\"");
                return $"\"{escaped}\"";
            }

            return val;
        }
    }
    #endregion
}
