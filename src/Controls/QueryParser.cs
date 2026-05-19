using System;
using System.Collections.Generic;
using System.Linq;

namespace SourceGit.Controls
{
    public static class QueryParser
    {
        private static ExprNode ParseInlineExpr(string value)
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

            var terms = segments.Select(s => new ExprNode { Op = ExprOp.Term, Value = s }).ToList();
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

        public static QuerySpec Parse(IEnumerable<string> tokens, IEnumerable<ITokenSuggestionProvider> providers)
        {
            var spec = new QuerySpec();
            var providersList = providers.ToList();

            var groups = new Dictionary<ITokenSuggestionProvider, (List<ExprNode> pos, List<ExprNode> neg)>();

            foreach (var token in tokens)
            {
                if (string.IsNullOrWhiteSpace(token)) continue;

                if (token is "||" or "&&" or "|" or "&")
                    continue;

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

                    var inlineExpr = ParseInlineExpr(val);
                    if (inlineExpr == null)
                        continue;

                    if (!groups.ContainsKey(matchedProvider))
                        groups[matchedProvider] = (new List<ExprNode>(), new List<ExprNode>());

                    if (isNegative)
                        groups[matchedProvider].neg.Add(inlineExpr);
                    else
                        groups[matchedProvider].pos.Add(inlineExpr);
                }
                else
                {
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

                var positiveExpr = MergeNodesByMode(posList, p.LogicMode);
                if (positiveExpr != null)
                    groupNodes.Add(positiveExpr);

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
