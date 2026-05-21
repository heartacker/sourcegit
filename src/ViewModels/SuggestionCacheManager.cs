using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace SourceGit.ViewModels
{
    internal class SuggestionCacheManager
    {
        private readonly List<Controls.ITokenSuggestionProvider> _providers;
        private readonly Func<string, Models.Commit, string, bool> _evalTerm;

        // Atomic bitmap cache: (Prefix, Value) -> BitArray
        private readonly Dictionary<(string, string), BitArray> _atomCache = new();

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

        private BitArray GetAtomBitmap(string prefix, string value, List<Models.Commit> rawCommits)
        {
            var key = (prefix, value);
            if (_atomCache.TryGetValue(key, out var cached))
                return cached;

            var bits = new BitArray(rawCommits.Count);
            for (int i = 0; i < rawCommits.Count; i++)
            {
                bits[i] = _evalTerm(prefix, rawCommits[i], value);
            }

            _atomCache[key] = bits;
            return bits;
        }

        private BitArray EvaluateExpr(Controls.ExprNode node, string prefix, List<Models.Commit> rawCommits)
        {
            if (node == null) return new BitArray(rawCommits.Count, true);

            switch (node.Op)
            {
                case Controls.ExprOp.Term:
                    return GetAtomBitmap(prefix, node.Value, rawCommits);

                case Controls.ExprOp.And:
                    if (node.Children == null || node.Children.Count == 0) return new BitArray(rawCommits.Count, true);
                    BitArray andRes = null;
                    foreach (var child in node.Children)
                    {
                        var res = EvaluateExpr(child, prefix, rawCommits);
                        if (andRes == null) andRes = new BitArray(res);
                        else andRes.And(res);
                    }
                    return andRes;

                case Controls.ExprOp.Or:
                    if (node.Children == null || node.Children.Count == 0) return new BitArray(rawCommits.Count, true);
                    BitArray orRes = null;
                    foreach (var child in node.Children)
                    {
                        var res = EvaluateExpr(child, prefix, rawCommits);
                        if (orRes == null) orRes = new BitArray(res);
                        else orRes.Or(res);
                    }
                    return orRes;

                case Controls.ExprOp.Not:
                    var notRes = new BitArray(EvaluateExpr(node.Children[0], prefix, rawCommits));
                    return notRes.Not();

                default:
                    return new BitArray(rawCommits.Count, true);
            }
        }

        public Dictionary<string, List<Models.Commit>> Compute(
            List<Models.Commit> rawCommits,
            List<Models.Commit> commits,
            Controls.QuerySpec spec)
        {
            _atomCache.Clear();
            var cache = new Dictionary<string, List<Models.Commit>>();

            // With sub-groups (parentheses): use _commits directly (fallback for now until AST rewrite)
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

            // Compute per-prefix cache: use Bitmaps to avoid repeated List creation
            var persistentPrefixes = new[] { "b:", "t:", "r:", "gitlog:", "sort:", "solo:" };
            var groupBitmaps = new Dictionary<string, BitArray>();

            // 1. Pre-evaluate each group's bitmap
            foreach (var group in spec.Groups)
            {
                if (persistentPrefixes.Contains(group.ProviderPrefix)) continue;
                groupBitmaps[group.ProviderPrefix] = EvaluateExpr(group.Expr, group.ProviderPrefix, rawCommits);
            }

            // 2. Compute suggestion cache for each prefix
            foreach (var provider in _providers)
            {
                var prefix = provider.Prefix;
                if (persistentPrefixes.Contains(prefix))
                {
                    cache[prefix] = rawCommits;
                    continue;
                }

                int targetPriority = GetProviderPriority(prefix);
                var finalBits = new BitArray(rawCommits.Count, true);

                foreach (var kvp in groupBitmaps)
                {
                    var otherPrefix = kvp.Key;
                    if (otherPrefix == prefix) continue;

                    int otherPriority = GetProviderPriority(otherPrefix);
                    if (otherPriority < targetPriority)
                    {
                        finalBits.And(kvp.Value);
                    }
                }

                // Convert BitArray back to List once
                var resultList = new List<Models.Commit>();
                for (int i = 0; i < rawCommits.Count; i++)
                {
                    if (finalBits[i]) resultList.Add(rawCommits[i]);
                }
                cache[prefix] = resultList;
            }

            return cache;
        }
    }
}
