using System;
using System.Collections.Generic;
using System.Linq;

namespace SourceGit.Controls
{
    public static class QueryParser
    {
        public static ExprNode ParseInlineExpr(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            var segments = new List<string>();
            var operators = new List<ExprOp>();
            var start = 0;

            for (int i = 0; i < value.Length - 1; i++)
            {
                if (value[i] == '|' && value[i + 1] == '|')
                {
                    var part = value[start..i].Trim();
                    if (!string.IsNullOrEmpty(part))
                        segments.Add(part);
                    operators.Add(ExprOp.Or);
                    start = i + 2;
                    i++;
                    continue;
                }

                if (value[i] == '&' && value[i + 1] == '&')
                {
                    var part = value[start..i].Trim();
                    if (!string.IsNullOrEmpty(part))
                        segments.Add(part);
                    operators.Add(ExprOp.And);
                    start = i + 2;
                    i++;
                }
            }

            var tail = value[start..].Trim();
            if (!string.IsNullOrEmpty(tail))
                segments.Add(tail);

            if (segments.Count == 0)
                return null;

            var terms = segments.Select(s => ExpandImplicitPrefix(s, segments[0])).ToList(); // Ensure all terms inherit the first segment's prefix
            if (operators.Count == 0)
                return terms[0];

            // First pass: collapse all AND into grouped nodes.
            var orBuckets = new List<ExprNode>();
            var currentAndBucket = new List<ExprNode> { terms[0] };
            for (int i = 0; i < operators.Count && i + 1 < terms.Count; i++)
            {
                var op = operators[i];
                var nextTerm = terms[i + 1];
                if (op == ExprOp.And)
                {
                    currentAndBucket.Add(nextTerm);
                }
                else
                {
                    orBuckets.Add(currentAndBucket.Count == 1
                        ? currentAndBucket[0]
                        : new ExprNode { Op = ExprOp.And, Children = currentAndBucket.ToList() });
                    currentAndBucket = new List<ExprNode> { nextTerm };
                }
            }

            orBuckets.Add(currentAndBucket.Count == 1
                ? currentAndBucket[0]
                : new ExprNode { Op = ExprOp.And, Children = currentAndBucket.ToList() });

            return orBuckets.Count == 1
                ? orBuckets[0]
                : new ExprNode { Op = ExprOp.Or, Children = orBuckets };
        }

        private static ExprNode ExpandImplicitPrefix(string term, string reference)
        {
            if (string.IsNullOrWhiteSpace(term))
                return null;

            var colonIndex = reference.IndexOf(':');
            if (colonIndex > 0 && colonIndex < reference.Length - 1)
            {
                var prefix = reference.Substring(0, colonIndex);
                return new ExprNode
                {
                    Op = ExprOp.Term,
                    Prefix = prefix,
                    Value = term
                };
            }

            return new ExprNode { Op = ExprOp.Term, Value = term };
        }

        private static ExprNode MergeNodesByMode(List<ExprNode> nodes, TokenLogicMode mode)
        {
            if (nodes == null || nodes.Count == 0)
                return null;

            if (mode == TokenLogicMode.SingleReplace)
                return nodes.Last();

            if (nodes.Count == 1)
                return nodes[0];

            var op = mode == TokenLogicMode.AutoAnd ? ExprOp.And : ExprOp.Or;
            return new ExprNode { Op = op, Children = nodes };
        }

        public static List<List<string>> PartitionByParentheses(IEnumerable<string> tokens)
        {
            var result = new List<List<string>>();
            var current = new List<string>();
            int depth = 0;
            bool hasParens = false;

            foreach (var token in tokens)
            {
                if (string.IsNullOrWhiteSpace(token)) continue;

                if (token == "(")
                {
                    hasParens = true;
                    if (depth == 0 && current.Count > 0)
                    {
                        result.Add(current);
                        current = new List<string>();
                    }
                    depth++;
                }
                else if (token == ")")
                {
                    if (depth > 0)
                    {
                        depth--;
                        if (depth == 0 && current.Count > 0)
                        {
                            result.Add(current);
                            current = new List<string>();
                        }
                    }
                }
                else
                {
                    current.Add(token);
                }
            }

            if (current.Count > 0 || (!hasParens && result.Count == 0))
                result.Add(current);

            return result;
        }

        public static QuerySpec Parse(IEnumerable<string> tokens, IEnumerable<ITokenSuggestionProvider> providers)
        {
            var tokenList = tokens.ToList();
            var providersList = providers.ToList();

            // Check for parenthesized grouping
            var subGroups = PartitionByParentheses(tokenList);
            if (subGroups.Count > 1)
            {
                // Multiple sub-groups: each is AND-ed internally, OR-ed across groups
                var spec = new QuerySpec();
                foreach (var groupTokens in subGroups)
                {
                    if (groupTokens.Count == 0) continue;
                    if (groupTokens.Count == 1 && (groupTokens[0] is "||" or "&&" or "|" or "&")) continue;
                    spec.SubGroups.Add(ParseGroup(groupTokens, providersList));
                }
                return spec;
            }

            // Check for cross-prefix || operators that should create OR sub-groups
            var orSubGroups = PartitionByCrossProviderOr(tokenList, providersList);
            if (orSubGroups.Count > 1)
            {
                var spec = new QuerySpec();
                foreach (var groupTokens in orSubGroups)
                {
                    if (groupTokens.Count == 0) continue;
                    spec.SubGroups.Add(ParseGroup(groupTokens, providersList));
                }
                return spec;
            }

            // Single group (no parens, no cross-prefix ||)
            return ParseGroup(tokenList, providersList);
        }

        private static List<List<string>> PartitionByCrossProviderOr(List<string> tokens, List<ITokenSuggestionProvider> providers)
        {
            // Build a quick provider lookup for each token
            var tokenProviders = new ITokenSuggestionProvider[tokens.Count];
            for (int i = 0; i < tokens.Count; i++)
            {
                var t = tokens[i];
                if (t is "||" or "&&" or "|" or "&" or "(" or ")")
                    continue;

                var check = t.StartsWith("-") ? t[1..] : t;
                foreach (var p in providers)
                {
                    if (check.StartsWith(p.Prefix, StringComparison.OrdinalIgnoreCase) ||
                        (p.FullPrefix != null && p.FullPrefix.Any(fp => check.StartsWith(fp, StringComparison.OrdinalIgnoreCase))))
                    {
                        tokenProviders[i] = p;
                        break;
                    }
                }
            }

            // Find split points: || between different providers
            var splitAfter = new HashSet<int>();
            for (int i = 0; i < tokens.Count; i++)
            {
                if (tokens[i] is "||" or "|")
                {
                    // Find previous non-operator token's provider
                    ITokenSuggestionProvider prevProvider = null;
                    for (int j = i - 1; j >= 0; j--)
                    {
                        if (tokenProviders[j] != null)
                        {
                            prevProvider = tokenProviders[j];
                            break;
                        }
                    }

                    // Find next non-operator token's provider
                    ITokenSuggestionProvider nextProvider = null;
                    for (int j = i + 1; j < tokens.Count; j++)
                    {
                        if (tokenProviders[j] != null)
                        {
                            nextProvider = tokenProviders[j];
                            break;
                        }
                    }

                    if (prevProvider != nextProvider)
                        splitAfter.Add(i);
                }
            }

            if (splitAfter.Count == 0)
                return [tokens];

            // Split tokens at the identified points
            var result = new List<List<string>>();
            var current = new List<string>();
            for (int i = 0; i < tokens.Count; i++)
            {
                current.Add(tokens[i]);
                if (splitAfter.Contains(i))
                {
                    result.Add(current);
                    current = new List<string>();
                }
            }

            if (current.Count > 0)
                result.Add(current);

            return result;
        }

        private static QuerySpec ParseGroup(List<string> tokens, List<ITokenSuggestionProvider> providersList)
        {
            var spec = new QuerySpec();

            var groups = new Dictionary<ITokenSuggestionProvider, (List<ExprNode> pos, List<ExprNode> neg)>();
            var explicitPosOps = new Dictionary<ITokenSuggestionProvider, List<ExprOp>>();
            ITokenSuggestionProvider lastPosProvider = null;
            ExprOp? pendingOp = null;

            foreach (var token in tokens)
            {
                if (string.IsNullOrWhiteSpace(token)) continue;

                if (token is "||" or "&&" or "|" or "&")
                {
                    pendingOp = token switch
                    {
                        "||" or "|" => ExprOp.Or,
                        "&&" or "&" => ExprOp.And,
                        _ => ExprOp.Or,
                    };
                    continue;
                }

                bool isNegative = token.StartsWith("-", StringComparison.Ordinal);
                string testToken = isNegative ? token[1..] : token;

                ITokenSuggestionProvider matchedProvider = null;
                string matchedPrefix = null;

                foreach (var p in providersList)
                {
                    if (testToken.StartsWith(p.Prefix, StringComparison.OrdinalIgnoreCase))
                    {
                        matchedProvider = p;
                        matchedPrefix = p.Prefix;
                        break;
                    }
                    if (p.FullPrefix != null)
                    {
                        foreach (var fp in p.FullPrefix)
                        {
                            if (testToken.StartsWith(fp, StringComparison.OrdinalIgnoreCase))
                            {
                                matchedProvider = p;
                                matchedPrefix = fp;
                                break;
                            }
                        }
                    }
                    if (matchedProvider != null) break;
                }

                if (matchedProvider != null)
                {
                    var val = testToken.Substring(matchedPrefix.Length).Trim();
                    if (string.IsNullOrWhiteSpace(val)) continue;

                    var inlineExpr = new ExprNode { Op = ExprOp.Term, Value = val };

                    // Track explicit operators between consecutive same-provider positive entries
                    if (!isNegative && lastPosProvider == matchedProvider && pendingOp.HasValue)
                    {
                        if (!explicitPosOps.ContainsKey(matchedProvider))
                            explicitPosOps[matchedProvider] = new List<ExprOp>();
                        explicitPosOps[matchedProvider].Add(pendingOp.Value);
                    }

                    if (!isNegative)
                        lastPosProvider = matchedProvider;
                    pendingOp = null;

                    if (!groups.ContainsKey(matchedProvider))
                        groups[matchedProvider] = (new List<ExprNode>(), new List<ExprNode>());

                    if (isNegative)
                        groups[matchedProvider].neg.Add(inlineExpr);
                    else
                        groups[matchedProvider].pos.Add(inlineExpr);
                }
                else
                {
                    lastPosProvider = null;
                    pendingOp = null;

                    if (isNegative)
                        spec.FallbackNotTerms.Add(testToken.Trim());
                    else
                        spec.FallbackTerms.Add(testToken.Trim());
                }
            }

            foreach (var kvp in groups)
            {
                var p = kvp.Key;
                var posList = kvp.Value.pos;
                var negList = kvp.Value.neg;

                var groupSpec = new GroupSpec { ProviderPrefix = p.Prefix };
                var groupNodes = new List<ExprNode>();

                ExprNode positiveExpr = null;
                if (posList.Count > 0)
                {
                    // Check if explicit operators override the default LogicMode
                    if (explicitPosOps.TryGetValue(p, out var ops) &&
                        ops.Count == posList.Count - 1 &&
                        posList.Count >= 2)
                    {
                        var distinct = ops.Distinct().ToList();
                        if (distinct.Count == 1)
                        {
                            var defaultOp = p.LogicMode == TokenLogicMode.AutoAnd ? ExprOp.And : ExprOp.Or;
                            if (distinct[0] != defaultOp)
                            {
                                // Explicit operator overrides LogicMode default
                                positiveExpr = new ExprNode { Op = distinct[0], Children = new List<ExprNode>(posList) };
                            }
                        }
                    }

                    if (positiveExpr == null)
                        positiveExpr = MergeNodesByMode(posList, p.LogicMode);

                    if (positiveExpr != null)
                        groupNodes.Add(positiveExpr);
                }

                foreach (var negExpr in negList)
                {
                    groupNodes.Add(new ExprNode
                    {
                        Op = ExprOp.Not,
                        Children = [negExpr]
                    });
                }

                if (groupNodes.Count > 0)
                {
                    if (groupNodes.Count == 1)
                    {
                        groupSpec.Expr = groupNodes[0];
                    }
                    else
                    {
                        groupSpec.Expr = new ExprNode { Op = ExprOp.And, Children = groupNodes };
                    }
                    spec.Groups.Add(groupSpec);
                }
            }

            return spec;
        }
    }
}
