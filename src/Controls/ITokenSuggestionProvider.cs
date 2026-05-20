using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SourceGit.Controls
{
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
        /// like sort:Commit-Date and   Topologically, the second token will replace the first one instead of being combined.
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

    public class TokenSuggestion
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public string Icon { get; set; }
        public bool IsSlashCommand { get; set; }
        public string SlashCommandName { get; set; }
        public string SlashCommandArgument { get; set; }
        public bool CanExecuteDirectly { get; set; }
        public TokenSuggestionActionType ActionType { get; set; } = TokenSuggestionActionType.Insert;
        public string ActionTooltip => ActionType == TokenSuggestionActionType.Execute ? "执行" : "上屏";

        // todo: use different icons for execute and insert actions
        // todo: add to resources:
        // "M2 6 L5 9 L10 2" for execute (a right arrow)
        // "M2 10 L4 10 L10 4 L8 2 L2 8 Z" for insert (a pencil)
        public string ActionIcon =>
            ActionType == TokenSuggestionActionType.Execute
                ? "M2 6 L5 9 L10 2"
                : "M2 10 L4 10 L10 4 L8 2 L2 8 Z";
    }

    /// <summary>
    ///     Represents a group for token suggestions, facilitating localization and structured display.
    /// </summary>
    public class TokenSuggestionGroup
    {
        public string Id { get; set; }
        public string Name { get; set; }

        public TokenSuggestionGroup(string id, string name)
        {
            Id = id;
            Name = name;
        }

        public override bool Equals(object obj) => obj is TokenSuggestionGroup other && Id == obj.ToString();
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
        string Icon { get; }

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
        ///     Returns a list of suggestions based on the user's current input after the prefix.
        /// </summary>
        /// <param name="pattern">The text user typed after the prefix.</param>
        /// <param name="cancellationToken">Cancellation token for aborting the request.</param>
        /// <returns>A list of suggested values.</returns>
        Task<IEnumerable<TokenSuggestion>> GetSuggestionsAsync(string pattern, CancellationToken cancellationToken);
    }

    /// <summary>
    ///     A simple implementation of ITokenSuggestionProvider that provides static suggestions.
    /// </summary>
    public class StaticTokenSuggestionProvider : ITokenSuggestionProvider
    {
        public string Prefix { get; }
        public string[] FullPrefix { get; }
        public string Description { get; }
        public string Icon { get; }
        public TokenSuggestionGroup Group { get; }
        public TokenLogicMode LogicMode { get; }
        public bool IsPersistent { get; }

        private readonly Func<string, CancellationToken, Task<IEnumerable<TokenSuggestion>>> _suggester;
        private readonly IEnumerable<TokenSuggestion> _staticOptions;

        public StaticTokenSuggestionProvider(string prefix, string description, TokenSuggestionGroup group = null,
            Func<string, CancellationToken, Task<IEnumerable<TokenSuggestion>>> suggester = null,
            TokenLogicMode logicMode = TokenLogicMode.AutoOr, string[] alias = null, string icon = null,
            bool isPersistent = false)
        {
            Prefix = prefix;
            FullPrefix = alias;
            Description = description;
            Icon = icon;
            Group = group;
            _suggester = suggester;
            LogicMode = logicMode;
            IsPersistent = isPersistent;
        }

        public StaticTokenSuggestionProvider(string prefix, string description, TokenSuggestionGroup group,
            IEnumerable<string> staticOptions, TokenLogicMode logicMode = TokenLogicMode.AutoOr,
            string[] alias = null, string icon = null, bool isPersistent = false)
        {
            Prefix = prefix;
            FullPrefix = alias;
            Description = description;
            Icon = icon;
            Group = group;
            _staticOptions = staticOptions.Select(x => new TokenSuggestion { Name = x }).ToList();
            LogicMode = logicMode;
            IsPersistent = isPersistent;
        }

        public Task<IEnumerable<TokenSuggestion>> GetSuggestionsAsync(string pattern, CancellationToken cancellationToken)
        {
            if (_suggester != null)
            {
                return _suggester(pattern, cancellationToken);
            }

            if (_staticOptions != null)
            {
                var filtered = _staticOptions.Where(x =>
                    string.IsNullOrEmpty(pattern) || x.Name.Contains(pattern, StringComparison.OrdinalIgnoreCase));
                return Task.FromResult(filtered);
            }

            return Task.FromResult(Enumerable.Empty<TokenSuggestion>());
        }
    }
}
