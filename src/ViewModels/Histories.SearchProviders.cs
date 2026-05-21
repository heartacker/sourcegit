using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SourceGit.Controls;

namespace SourceGit.ViewModels
{
    // ReSharper disable once PartialTypeWithSinglePart
    partial class Histories
    {
        private static List<string> BuildGitOptionTokens(Models.HistoryShowFlags flags)
        {
            var tokens = new List<string>();
            if (flags.HasFlag(Models.HistoryShowFlags.Reflog))
                tokens.Add("gitlog:reflog");
            if (flags.HasFlag(Models.HistoryShowFlags.FirstParentOnly))
                tokens.Add("gitlog:1st-p");
            if (flags.HasFlag(Models.HistoryShowFlags.SimplifyByDecoration))
                tokens.Add("gitlog:decora");
            return tokens;
        }

        private void SyncGitTokensFromFlags(Models.HistoryShowFlags flags)
        {
            var desired = BuildGitOptionTokens(flags);
            var current = SearchTokens
                .Where(t => t.StartsWith("gitlog:", StringComparison.OrdinalIgnoreCase))
                .ToList();

            bool same = desired.Count == current.Count;
            if (same)
            {
                foreach (var token in desired)
                {
                    if (!current.Any(c => c.Equals(token, StringComparison.OrdinalIgnoreCase)))
                    {
                        same = false;
                        break;
                    }
                }
            }

            if (same)
                return;

            _suppressSearchTokenCollectionChanged = true;
            try
            {
                foreach (var old in current)
                    SearchTokens.Remove(old);

                foreach (var token in desired)
                    SearchTokens.Add(token);
            }
            finally
            {
                _suppressSearchTokenCollectionChanged = false;
            }
        }

        private Task<IEnumerable<TokenSuggestion>> SuggestAuthors(string pattern, CancellationToken ct)
        {
            var source = _suggestionCache.GetValueOrDefault("a:") ?? _commits;
            var authors = source.Select(c => c.Author).DistinctBy(a => a.Name);
            var suggestions = authors
                .Where(a => string.IsNullOrEmpty(pattern) || a.Name.Contains(pattern, StringComparison.OrdinalIgnoreCase) || a.Email.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                .Select(a => new TokenSuggestion { Name = a.Name, Description = a.Email });
            return Task.FromResult(suggestions);
        }

        private Task<IEnumerable<TokenSuggestion>> SuggestBranches(string pattern, CancellationToken ct)
        {
            var branchSource = _suggestionCache.GetValueOrDefault("b:") ?? _commits;
            var branchNames = branchSource
                .SelectMany(c => c.Decorators)
                .Where(d => d.Type is Models.DecoratorType.LocalBranchHead or Models.DecoratorType.CurrentBranchHead)
                .Select(d => d.Name)
                .Where(n => !string.IsNullOrEmpty(n))
                .Distinct(StringComparer.OrdinalIgnoreCase);

            var suggestions = branchNames
                .Where(n => string.IsNullOrEmpty(pattern) || n.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                .OrderBy(n => n)
                .Take(100)
                .Select(n => new TokenSuggestion { Name = n });
            return Task.FromResult(suggestions);
        }

        private Task<IEnumerable<TokenSuggestion>> SuggestRemotes(string pattern, CancellationToken ct)
        {
            var remoteSource = _suggestionCache.GetValueOrDefault("r:") ?? _commits;
            var remoteBranches = remoteSource
                .SelectMany(c => c.Decorators)
                .Where(d => d.Type == Models.DecoratorType.RemoteBranchHead)
                .Select(d => d.Name)
                .Where(n => !string.IsNullOrEmpty(n))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            // Suggest remote names (prefixes like "G") for folder-level filtering
            var remoteNames = remoteBranches
                .Select(n => n.Contains('/') ? n[..n.IndexOf('/')] : n)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(n => new TokenSuggestion { Name = n, Description = "所有远程分支" });

            // Suggest specific remote branch names (like "G/H")
            var branchSuggestions = remoteBranches
                .Where(n => string.IsNullOrEmpty(pattern) || n.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                .OrderBy(n => n)
                .Take(100)
                .Select(n => new TokenSuggestion { Name = n });

            var suggestions = branchSuggestions.ToList();
            foreach (var rn in remoteNames)
            {
                if (string.IsNullOrEmpty(pattern) || rn.Name.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                {
                    if (!suggestions.Any(s => s.Name == rn.Name))
                        suggestions.Insert(0, rn);
                }
            }

            return Task.FromResult((IEnumerable<TokenSuggestion>)suggestions);
        }

        private Task<IEnumerable<TokenSuggestion>> SuggestTags(string pattern, CancellationToken ct)
        {
            var tagSource = _suggestionCache.GetValueOrDefault("t:") ?? _commits;
            var tagNames = tagSource
                .SelectMany(c => c.Decorators)
                .Where(d => d.Type == Models.DecoratorType.Tag)
                .Select(d => d.Name)
                .Where(n => !string.IsNullOrEmpty(n))
                .Distinct(StringComparer.OrdinalIgnoreCase);

            var suggestions = tagNames
                .Where(n => string.IsNullOrEmpty(pattern) || n.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                .OrderBy(n => n)
                .Take(100)
                .Select(n => new TokenSuggestion { Name = n });
            return Task.FromResult(suggestions);
        }

        private Task<IEnumerable<TokenSuggestion>> SuggestMessages(string pattern, CancellationToken ct)
        {
            var messageSource = _suggestionCache.GetValueOrDefault("m:") ?? _commits;
            var subjects = messageSource
                .Select(c => c.Subject)
                .Where(s => !string.IsNullOrEmpty(s))
                .Distinct(StringComparer.OrdinalIgnoreCase);

            var suggestions = subjects
                .Where(s => string.IsNullOrEmpty(pattern) || s.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                .Take(100)
                .Select(s => new TokenSuggestion { Name = s });
            return Task.FromResult(suggestions);
        }

        private Task<IEnumerable<TokenSuggestion>> SuggestSoloCommits(string pattern, CancellationToken ct)
        {
            var query = pattern?.Trim() ?? string.Empty;
            var soloSource = _suggestionCache.GetValueOrDefault("solo:") ?? _commits;
            var commits = soloSource
                .Where(c => !string.IsNullOrWhiteSpace(c?.SHA))
                .Where(c =>
                    string.IsNullOrEmpty(query) ||
                    (!string.IsNullOrEmpty(c.Subject) && c.Subject.Contains(query, StringComparison.OrdinalIgnoreCase)) ||
                    c.SHA.StartsWith(query, StringComparison.OrdinalIgnoreCase))
                .Take(100)
                .Select(c => new TokenSuggestion
                {
                    Name = string.IsNullOrWhiteSpace(c.Subject) ? c.SHA[..Math.Min(10, c.SHA.Length)] : c.Subject,
                    Value = c.SHA,
                    Description = $"{c.SHA[..Math.Min(10, c.SHA.Length)]} · {c.Author.Name}",
                });

            // NOTE: this also uses _commits so HEAD only appears when it's in the filtered view
            var head = soloSource.FirstOrDefault(x => x.IsCurrentHead);
            if (head != null && (string.IsNullOrEmpty(query) ||
                                 "HEAD".Contains(query, StringComparison.OrdinalIgnoreCase) ||
                                 (!string.IsNullOrEmpty(head.Subject) && head.Subject.Contains(query, StringComparison.OrdinalIgnoreCase))))
            {
                commits = new[]
                {
                    new TokenSuggestion
                    {
                        Name = "HEAD",
                        Value = "HEAD",
                        Description = string.IsNullOrWhiteSpace(head.Subject) ? "当前分支头提交" : head.Subject,
                    }
                }.Concat(commits);
            }

            return Task.FromResult(commits.DistinctBy(c => c.Value ?? c.Name));
        }

        private void SetupSearchProviders()
        {
            var implementedIcon = "M 1.5 6.5 L 4.5 9.5 L 10.5 2.5";

            var groupGit = new TokenSuggestionGroup("git", "Git 选项", 0);
            var groupFilters = new TokenSuggestionGroup("filters", "常规过滤", 1);
            var groupView = new TokenSuggestionGroup("view", "视图控制", 2);
            var groupAdvanced = new TokenSuggestionGroup("advanced", "高级检索", 3);

            var isProv = new[] {
                "merged", "unmerged", "tag",
                "branch", "merge", "cherrypick",
                "head", "folded" };

            SearchProviders.Add(new StaticTokenSuggestionProvider("is:", "状态过滤",
                groupAdvanced, isProv, TokenLogicMode.AutoAnd, icon: implementedIcon, priority: 11));
            SearchProviders.Add(new StaticTokenSuggestionProvider("a:", "作者", groupFilters,
                suggester: SuggestAuthors, logicMode: TokenLogicMode.AutoOr,
                alias: new[] { "author:" }, icon: implementedIcon, priority: 31));
            SearchProviders.Add(new StaticTokenSuggestionProvider("m:", "提交消息", groupFilters,
                suggester: SuggestMessages, logicMode: TokenLogicMode.AutoOr,
                alias: new[] { "message:" }, icon: implementedIcon, priority: 101));
            SearchProviders.Add(new StaticTokenSuggestionProvider("b:", "分支", groupFilters,
                suggester: SuggestBranches, logicMode: TokenLogicMode.AutoOr,
                alias: new[] { "branch:" }, icon: implementedIcon, isPersistent: true, priority: 0));
            SearchProviders.Add(new StaticTokenSuggestionProvider("solo:", "Solo 提交链过滤", groupView,
                suggester: SuggestSoloCommits, logicMode: TokenLogicMode.AutoOr,
                alias: new[] { "sole:" }, icon: implementedIcon, priority: 1));
            SearchProviders.Add(new StaticTokenSuggestionProvider("t:", "标签", groupFilters,
                suggester: SuggestTags, alias: new[] { "tag:" }, icon: implementedIcon,
                isPersistent: true, priority: 0));
            SearchProviders.Add(new StaticTokenSuggestionProvider("r:", "远程分支", groupFilters,
                suggester: SuggestRemotes, alias: new[] { "remote:" }, icon: implementedIcon,
                isPersistent: true, priority: 0));
            SearchProviders.Add(new StaticTokenSuggestionProvider("f:", "文件路径", groupFilters,
                alias: new[] { "file:" }, priority: 41));
            SearchProviders.Add(new StaticTokenSuggestionProvider("p:", "路径", groupFilters,
                alias: new[] { "path:" }, priority: 41));
            SearchProviders.Add(new StaticTokenSuggestionProvider("sha:", "哈希", groupFilters,
                icon: implementedIcon, priority: 41));
            SearchProviders.Add(new StaticTokenSuggestionProvider("since:", "起始时间", groupFilters,
                alias: new[] { "after:" }, icon: implementedIcon, priority: 51));
            SearchProviders.Add(new StaticTokenSuggestionProvider("until:", "结束时间", groupFilters,
                alias: new[] { "before:" }, icon: implementedIcon, priority: 51));

            SearchProviders.Add(new StaticTokenSuggestionProvider("c:", "提交者", groupAdvanced,
                alias: new[] { "committer:" }, icon: implementedIcon, priority: 31));
            SearchProviders.Add(new StaticTokenSuggestionProvider("e:", "邮箱", groupAdvanced,
                alias: new[] { "email:" }, icon: implementedIcon, priority: 31));
            SearchProviders.Add(new StaticTokenSuggestionProvider("S:", "内容搜索 (Pickaxe)", groupAdvanced, priority: 61));
            SearchProviders.Add(new StaticTokenSuggestionProvider("G:", "正则搜索 (Grep)", groupAdvanced, priority: 61));
            SearchProviders.Add(new StaticTokenSuggestionProvider("change:", "变更类型", groupAdvanced, priority: 61));
            SearchProviders.Add(new StaticTokenSuggestionProvider("signed:", "GPG 签名状态", groupAdvanced, priority: 61));
            SearchProviders.Add(new StaticTokenSuggestionProvider("parent:", "父提交搜索", groupAdvanced, priority: 61));

            SearchProviders.Add(new StaticTokenSuggestionProvider("sort:", "排序方式", groupView,
                new[] { "Commit Date", "Topologically" }, TokenLogicMode.SingleReplace, priority: 999));

            SearchProviders.Add(new StaticTokenSuggestionProvider("gitlog:", "git 解析选项", groupGit,
                new[] { "reflog", "1st-p", "decora" }, TokenLogicMode.AutoOr, isPersistent: true, priority: 0));
        }

        private BitArray EvaluateToBitmap(Controls.QuerySpec spec)
        {
            var totalCount = _rawCommits.Count;
            var bits = new BitArray(totalCount, true);

            // 1. Apply groups (a:, m:, is:, etc.)
            foreach (var group in spec.Groups)
            {
                if (group.ProviderPrefix is "gitlog:" or "sort:" or "b:" or "t:" or "r:")
                    continue;

                // Solo: special handling
                if (group.ProviderPrefix == "solo:")
                {
                    // For now, solo still uses List-based FilterCommits in the main chain,
                    // but we could bitmapize it later.
                    continue;
                }

                var groupBits = new BitArray(totalCount);
                for (int i = 0; i < totalCount; i++)
                {
                    groupBits[i] = Controls.ExprEvaluator.Evaluate(group.Expr, val => EvalTerm(group.ProviderPrefix, _rawCommits[i], val));
                }
                bits.And(groupBits);
            }

            // 2. Apply fallback terms
            if (spec.FallbackTerms.Count > 0 || spec.FallbackNotTerms.Count > 0)
            {
                var fallbackBits = new BitArray(totalCount);
                for (int i = 0; i < totalCount; i++)
                {
                    var msg = _rawCommits[i].Subject ?? string.Empty;
                    var include = spec.FallbackTerms.Count == 0 ||
                                  spec.FallbackTerms.Any(f => msg.Contains(f, StringComparison.OrdinalIgnoreCase));
                    var exclude = spec.FallbackNotTerms.Any(f => msg.Contains(f, StringComparison.OrdinalIgnoreCase));
                    fallbackBits[i] = include && !exclude;
                }
                bits.And(fallbackBits);
            }

            return bits;
        }

        private void OnSearchTokensChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            if (_suppressSearchTokenCollectionChanged)
                return;

            var gitOptions = SearchTokens
                .Where(t => t.StartsWith("gitlog:", StringComparison.OrdinalIgnoreCase))
                .Select(t => t.Substring(t.IndexOf(':') + 1).Trim())
                .Where(t => !string.IsNullOrEmpty(t))
                .ToList();

            if (gitOptions.Count > 0)
            {
                var flags = Models.HistoryShowFlags.None;
                if (gitOptions.Any(o => o.Equals("reflog", StringComparison.OrdinalIgnoreCase)))
                    flags |= Models.HistoryShowFlags.Reflog;
                if (gitOptions.Any(o => o.Equals("1st-p", StringComparison.OrdinalIgnoreCase)))
                    flags |= Models.HistoryShowFlags.FirstParentOnly;
                if (gitOptions.Any(o => o.Equals("decora", StringComparison.OrdinalIgnoreCase)))
                    flags |= Models.HistoryShowFlags.SimplifyByDecoration;

                if (_repo.HistoryShowFlags != flags)
                {
                    _syncingRepoFlagsFromTokens = true;
                    _repo.HistoryShowFlags = flags;
                    _syncingRepoFlagsFromTokens = false;
                }

                _gitOptionsDrivenByTokens = true;
            }
            else if (_gitOptionsDrivenByTokens)
            {
                if (_repo.HistoryShowFlags != Models.HistoryShowFlags.None)
                {
                    _syncingRepoFlagsFromTokens = true;
                    _repo.HistoryShowFlags = Models.HistoryShowFlags.None;
                    _syncingRepoFlagsFromTokens = false;
                }

                _gitOptionsDrivenByTokens = false;
            }

            // Handle b:/t:/r: tokens via HistoryFilters for git log re-execution
            var newSearchFilters = new List<Models.HistoryFilter>();
            foreach (var token in SearchTokens)
            {
                var isNeg = token.StartsWith("-", StringComparison.Ordinal);
                var check = isNeg ? token[1..] : token;
                var mode = isNeg ? Models.FilterMode.Excluded : Models.FilterMode.Included;

                if (check.StartsWith("b:", StringComparison.OrdinalIgnoreCase) ||
                    check.StartsWith("branch:", StringComparison.OrdinalIgnoreCase))
                {
                    var val = check[(check.IndexOf(':') + 1)..].Trim();
                    if (val.Length > 0)
                        newSearchFilters.Add(new Models.HistoryFilter($"refs/heads/{val}", Models.FilterType.LocalBranch, mode));
                }
                else if (check.StartsWith("t:", StringComparison.OrdinalIgnoreCase) ||
                         check.StartsWith("tag:", StringComparison.OrdinalIgnoreCase))
                {
                    var val = check[(check.IndexOf(':') + 1)..].Trim();
                    if (val.Length > 0)
                        newSearchFilters.Add(new Models.HistoryFilter(val, Models.FilterType.Tag, mode));
                }
                else if (check.StartsWith("r:", StringComparison.OrdinalIgnoreCase) ||
                         check.StartsWith("remote:", StringComparison.OrdinalIgnoreCase))
                {
                    var val = check[(check.IndexOf(':') + 1)..].Trim();
                    if (val.Length > 0)
                    {
                        // No "/" means it's a remote name (e.g. "origin"), not a specific ref → use folder type
                        var type = val.Contains('/') ? Models.FilterType.RemoteBranch : Models.FilterType.RemoteBranchFolder;
                        newSearchFilters.Add(new Models.HistoryFilter($"refs/remotes/{val}", type, mode));
                    }
                }
            }

            // Cancel out entries where same (Pattern, Type) has both Included and Excluded
            var seen = new Dictionary<(string, Models.FilterType), Models.FilterMode>();
            var toCancel = new HashSet<(string, Models.FilterType)>();
            foreach (var f in newSearchFilters)
            {
                var key = (f.Pattern, f.Type);
                if (seen.TryGetValue(key, out var existing))
                {
                    if (existing != f.Mode)
                        toCancel.Add(key);
                }
                else
                {
                    seen[key] = f.Mode;
                }
            }
            newSearchFilters.RemoveAll(f => toCancel.Contains((f.Pattern, f.Type)));

            var changed = newSearchFilters.Count != _searchDrivenFilters.Count;
            if (!changed)
            {
                for (int i = 0; i < newSearchFilters.Count; i++)
                {
                    var a = newSearchFilters[i];
                    var b = _searchDrivenFilters[i];
                    if (a.Pattern != b.Pattern || a.Type != b.Type || a.Mode != b.Mode)
                    {
                        changed = true;
                        break;
                    }
                }
            }

            if (changed)
            {
                foreach (var old in _searchDrivenFilters)
                    _repo.UIStates.HistoryFilters.Remove(old);
                _searchDrivenFilters.Clear();

                foreach (var nf in newSearchFilters)
                {
                    // Skip if already present (e.g. added by sidebar via branch:/tag:/remote: tokens)
                    var exists = _repo.UIStates.HistoryFilters.Any(f =>
                        f.Pattern == nf.Pattern && f.Type == nf.Type && f.Mode == nf.Mode);
                    if (!exists)
                    {
                        _repo.UIStates.HistoryFilters.Add(nf);
                        _searchDrivenFilters.Add(nf);
                    }
                }

                _repo.RefreshCommits();
            }

            UpdateDisplayCommits();
        }
    }
}
