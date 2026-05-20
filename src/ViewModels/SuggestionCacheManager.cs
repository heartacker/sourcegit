using System;
using System.Collections.Generic;
using System.Linq;

namespace SourceGit.ViewModels
{
    internal class SuggestionCacheManager
    {
        private readonly List<Controls.ITokenSuggestionProvider> _providers;
        private readonly Func<string, Models.Commit, string, bool> _evalTerm;

        public SuggestionCacheManager(
            IEnumerable<Controls.ITokenSuggestionProvider> providers,
            Func<string, Models.Commit, string, bool> evalTerm)
        {
            _providers = providers.ToList();
            _evalTerm = evalTerm;
        }

        private int GetProviderPriority(string prefix)
        {
            foreach (var p in _providers)
            {
                if (p.Prefix == prefix)
                    return p.Priority;
            }
            return 999;
        }

        public Dictionary<string, List<Models.Commit>> Compute(
            List<Models.Commit> rawCommits,
            List<Models.Commit> commits,
            Controls.QuerySpec spec)
        {
            var cache = new Dictionary<string, List<Models.Commit>>();

            // With sub-groups (parentheses): use _commits directly
            if (spec.SubGroups.Count > 0)
            {
                foreach (var provider in _providers)
                {
                    var p = provider.Prefix;
                    if (p is "sort:" or "gitlog:")
                        continue;
                    cache[p] = commits;
                }
                return cache;
            }

            // Compute per-prefix cache: rawCommits filtered by higher-priority groups only
            var persistentPrefixes = new[] { "b:", "t:", "r:", "gitlog:", "sort:", "solo:" };
            foreach (var group in spec.Groups)
            {
                var prefix = group.ProviderPrefix;

                // Skip persistent/special groups (they don't use EvalTerm)
                if (persistentPrefixes.Contains(prefix))
                    continue;

                int targetPriority = GetProviderPriority(prefix);

                var cacheSource = new List<Models.Commit>(rawCommits);
                foreach (var otherGroup in spec.Groups)
                {
                    var otherPrefix = otherGroup.ProviderPrefix;

                    // Skip self
                    if (otherPrefix == prefix)
                        continue;

                    // Skip persistent/special groups (already applied at git level)
                    if (persistentPrefixes.Contains(otherPrefix))
                        continue;

                    // Only apply higher priority (lower number = higher priority)
                    int otherPriority = GetProviderPriority(otherPrefix);
                    if (otherPriority >= targetPriority)
                        continue;

                    var otherExpr = otherGroup.Expr;
                    cacheSource = cacheSource
                        .Where(c => Controls.ExprEvaluator.Evaluate(otherExpr,
                            val => _evalTerm(otherPrefix, c, val)))
                        .ToList();
                }

                cache[prefix] = cacheSource;
            }

            // Persistent prefixes: cache = rawCommits (priority 0, nothing is higher)
            foreach (var persistent in persistentPrefixes)
            {
                if (!cache.ContainsKey(persistent) && persistent is not ("sort:" or "gitlog:"))
                    cache[persistent] = rawCommits;
            }

            return cache;
        }
    }
}
