using System;
using System.Collections.Generic;
using System.Linq;

namespace SourceGit.Controls
{
    public static class QueryParser
    {
        public static QuerySpec Parse(IEnumerable<string> tokens, IEnumerable<ITokenSuggestionProvider> providers)
        {
            var spec = new QuerySpec();
            var providersList = providers.ToList();

            var groups = new Dictionary<ITokenSuggestionProvider, (List<string> pos, List<string> neg)>();

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

                    if (!groups.ContainsKey(matchedProvider))
                        groups[matchedProvider] = (new List<string>(), new List<string>());

                    if (isNegative)
                        groups[matchedProvider].neg.Add(val);
                    else
                        groups[matchedProvider].pos.Add(val);
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

                if (posList.Count > 0)
                {
                    if (p.LogicMode == TokenLogicMode.SingleReplace)
                    {
                        groupNodes.Add(new ExprNode { Op = ExprOp.Term, Value = posList.Last() });
                    }
                    else
                    {
                        var op = p.LogicMode == TokenLogicMode.AutoAnd ? ExprOp.And : ExprOp.Or;
                        var posNode = posList.Count == 1
                            ? new ExprNode { Op = ExprOp.Term, Value = posList[0] }
                            : new ExprNode { Op = op, Children = posList.Select(v => new ExprNode { Op = ExprOp.Term, Value = v }).ToList() };
                        groupNodes.Add(posNode);
                    }
                }

                foreach (var nv in negList)
                {
                    groupNodes.Add(new ExprNode
                    {
                        Op = ExprOp.Not,
                        Children = [ new ExprNode { Op = ExprOp.Term, Value = nv } ]
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
