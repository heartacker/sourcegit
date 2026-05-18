using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SourceGit.Controls {
    /// <summary>
    ///     Represents a suggestion item for the token search box.
    /// </summary>
    public class TokenSuggestion {
        public string Name { get; set; }
        public string Description { get; set; }
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
        ///     An optional description for this provider (e.g., "Filter by author").
        /// </summary>
        string Description { get; }

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
        public string Description { get; }

        private readonly Func<string, CancellationToken, Task<IEnumerable<TokenSuggestion>>> _suggester;

        public StaticTokenSuggestionProvider(string prefix, string description, Func<string, CancellationToken, Task<IEnumerable<TokenSuggestion>>> suggester = null) {
            Prefix = prefix;
            Description = description;
            _suggester = suggester;
        }

        public Task<IEnumerable<TokenSuggestion>> GetSuggestionsAsync(string pattern, CancellationToken cancellationToken) {
            if (_suggester != null) {
                return _suggester(pattern, cancellationToken);
            }

            return Task.FromResult(Enumerable.Empty<TokenSuggestion>());
        }
    }
}
