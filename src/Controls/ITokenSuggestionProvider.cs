using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SourceGit.Controls {
    /// <summary>
    ///     Defines the logic mode for how tokens from a provider should be combined.
    /// </summary>
    public enum TokenLogicMode {
        None,
        SingleReplace,
        AutoOr
    }

    /// <summary>
    ///     Represents a suggestion item for the token search box.
    /// </summary>
    public class TokenSuggestion {
        public string Name { get; set; }
        public string Description { get; set; }
        public string Icon { get; set; }
    }

    /// <summary>
    ///     Represents a group for token suggestions, facilitating localization and structured display.
    /// </summary>
    public class TokenSuggestionGroup {
        public string Id { get; set; }
        public string Name { get; set; }

        public TokenSuggestionGroup(string id, string name) {
            Id = id;
            Name = name;
        }
    }

    /// <summary>
    ///     Represents a group header in the suggestions list.
    /// </summary>
    public class TokenSuggestionHeader {
        public string Name { get; set; }
    }

    /// <summary>
    ///     Provides suggestions for a specific token prefix.
    /// </summary>
    public interface ITokenSuggestionProvider {
        /// <summary>
        ///     The prefix that triggers this provider (e.g., "author:", "b:").
        /// </summary>
        string Prefix { get; }

        /// <summary>
        ///     Optional alternative prefixes that also trigger this provider (e.g., "a:" for "author:").
        /// </summary>
        string[] Aliases { get; }

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
    public class StaticTokenSuggestionProvider : ITokenSuggestionProvider {
        public string Prefix { get; }
        public string[] Aliases { get; }
        public string Description { get; }
        public string Icon { get; }
        public TokenSuggestionGroup Group { get; }
        public TokenLogicMode LogicMode { get; }

        private readonly Func<string, CancellationToken, Task<IEnumerable<TokenSuggestion>>> _suggester;
        private readonly IEnumerable<TokenSuggestion> _staticOptions;

        public StaticTokenSuggestionProvider(string prefix, string description, TokenSuggestionGroup group = null, Func<string, CancellationToken, Task<IEnumerable<TokenSuggestion>>> suggester = null, TokenLogicMode logicMode = TokenLogicMode.None, string[] alias = null, string icon = null) {
            Prefix = prefix;
            Aliases = alias;
            Description = description;
            Icon = icon;
            Group = group;
            _suggester = suggester;
            LogicMode = logicMode;
        }

        public StaticTokenSuggestionProvider(string prefix, string description, TokenSuggestionGroup group, IEnumerable<string> staticOptions, TokenLogicMode logicMode = TokenLogicMode.None, string[] alias = null, string icon = null) {
            Prefix = prefix;
            Aliases = alias;
            Description = description;
            Icon = icon;
            Group = group;
            _staticOptions = staticOptions.Select(x => new TokenSuggestion { Name = x }).ToList();
            LogicMode = logicMode;
        }

        public Task<IEnumerable<TokenSuggestion>> GetSuggestionsAsync(string pattern, CancellationToken cancellationToken) {
            if (_suggester != null) {
                return _suggester(pattern, cancellationToken);
            }

            if (_staticOptions != null) {
                var filtered = _staticOptions.Where(x => string.IsNullOrEmpty(pattern) || x.Name.Contains(pattern, StringComparison.OrdinalIgnoreCase));
                return Task.FromResult(filtered);
            }

            return Task.FromResult(Enumerable.Empty<TokenSuggestion>());
        }
    }
}
