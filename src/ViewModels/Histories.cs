// Token Filter Logic:
//   SearchTokens are parsed by QueryParser into groups by prefix.
//   Each group is either handled in-memory (UpdateDisplayCommits) or
//   triggers git log re-execution via HistoryFilters (BuildHistoryParams).
//
//   In-memory filters (a:, m:, is:, s:, since:, until:, f:, p:, ...):
//     evaluated per-commit against _rawCommits in UpdateDisplayCommits.
//
//   Persistent git log filters (b:/branch:, t:/tag:, r:/remote:, gitlog:):
//     SKIPPED in UpdateDisplayCommits (skip list at group processing).
//     Instead, their CollectionChanged handler creates HistoryFilter
//     entries in RepositoryUIStates.HistoryFilters, which feeds into
//     BuildHistoryParams() to rebuild git log arguments.
//
//   HistoryFilters flow:
//     SearchTokens -(b:/t:/r:)-> HistoryFilters -(BuildHistoryParams)-> git log
//     Sidebar UI ---(branch:/tag:/remote:)-> HistoryFilters -(same)->
//
//   Negation (-b:xxx):
//     TokenSearchBox.AddToken replaces opposite versions (-b:main <-> b:main).
//     Parser creates Excluded-mode HistoryFilter entries.
//     BuildHistoryParams uses ^prefix for excludes when includes exist,
//     or --exclude with --branches/--remotes/--tags for exclude-only.
//
//   Cancellation:
//     Same (pattern, type) with both Included and Excluded -> both removed.
//
//   ====== 数据流 / Data Flow ======
//   _rawCommits = git log 原始输出，仅受持久过滤器影响（b:, t:, r:, gitlog:）。
//                 这些持久过滤器通过重新执行 git log 来筛选（AND 关系），
//                 不受内存过滤器影响（a:, m:, is: 等）。
//   _commits    = _rawCommits 经过 UpdateDisplayCommits() 应用所有内存过滤器后的结果。
//                 这就是实际显示的提交列表。
//
//   ====== 建议器优先级缓存 / Suggester Priority Cache ======
//   每个建议器从 _suggestionCache[prefix] 读取，而非直接读 _commits。
//   _suggestionCache[P] = _rawCommits 经过优先级高于 P 的组筛选后的结果。
//   优先级数字越小优先级越高（0 = 最高）。
//
//   优先级体系:
//     0: b:, t:, r:, gitlog:   (git log 级别，缓存 = _rawCommits)
//     1: solo:                 (Solo 提交链)
//    11: is:                   (状态筛选)
//    31: a:, c:, e:            (身份)
//    41: s:, f:, p:            (搜索)
//    51: since:, until:        (时间)
//    61: S:, G:, change:, signed:, parent: (高级 git)
//   101: m:                    (消息 — 最低)
//   999: sort:                 (不是过滤器)
//
//   缓存算法:
//     cache[P] = _rawCommits
//     对每个其他组 G:
//       跳过自身和持久组(b:/t:/r:/gitlog:)
//       仅当 priority(G) < priority(P) 时（G 优先级更高）才应用
//       应用方式: ExprEvaluator.Evaluate(G.Expr, val => EvalTerm(G.prefix, c, val))
//     持久组(b:/t:/r:)缓存 = _rawCommits（nothing is higher priority）
//
//   示例 "a:acker m:fix"：
//     cache["a:"] = _rawCommits（没有优先级高于 a: 的内存组）
//     cache["m:"] = _rawCommits 经 a: 筛选（a: 优先级高于 m:）
//     → 编辑 a: 时显示所有作者，编辑 m: 时只显示 acker 的提交信息
//
//   ====== 括号分组 / Parentheses Grouping ======
//   输入 (a:acker m:bug) (a:bob m:fix) 实现跨前缀 OR。
//   QueryParser.PartitionByParentheses() 将 token 按 () 切分为子组。
//   每个子组内部 AND 连接，子组之间 OR 连接（Union DistinctBy SHA）。
//   ( 和 ) 作为运算符 Token，无删除按钮，气泡半透明粗体显示。
//
//   ====== 逻辑模式 / Logic Modes ======
//   AutoOr (同前缀 OR):
//     相同前缀的 token 之间是 OR 关系（并集）。
//     例如 "a:acker a:bob" → 显示 acker 或 bob 的提交。
//     适用前缀: a:, m:, b:, t:, r:, gitlog:
//   AutoAnd (同前缀 AND):
//     相同前缀的 token 之间是 AND 关系（交集）。
//     例如 "is:merged is:tag" → 既是合并提交又打了标签的提交。
//     适用前缀: is:
//   SingleReplace (同前缀替换):
//     只允许一个值，新值替换旧值。
//     适用前缀: sort:
//   不同前缀之间始终是 AND 关系，与各自模式无关：
//     例如 "a:acker m:fix" → 作者为 acker 且提交消息包含 fix
//
//   ====== AutoOr 建议作用域（由缓存保证） ======
//   _commits = (所有 a: OR) AND (所有 m: OR) AND (所有 is: AND) AND ...
//
//   建议器不再直接读 _commits，而是读 _suggestionCache[其前缀]。
//   缓存中已排除了自身同前缀的组，因此:
//     - AutoOr 前缀：建议范围被更高优先级前缀缩小，但不受自身同前缀影响
//     - AutoAnd 前缀：建议范围被更高优先级前缀缩小，也被自身同前缀缩小（因为 AND 会缩小结果）
//     - 持久前缀(b:/t:/r:)：缓存 = _rawCommits，完全不受内存过滤器影响

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SourceGit.ViewModels
{
    public class Histories : ObservableObject
    {
        public Repository Repo => _repo;

        public string SearchText
        {
            get => _searchText;
            set => SetProperty(ref _searchText, value);
        }

        public ObservableCollection<string> SearchTokens { get; } = new();

        public ObservableCollection<Controls.ITokenSuggestionProvider> SearchProviders { get; } = new();

        public ObservableCollection<Controls.TokenSlashCommand> SearchSlashCommands { get; } = new();

        public bool IsLoading
        {
            get => _isLoading;
            set => SetProperty(ref _isLoading, value);
        }

        public bool IsAuthorColumnVisible
        {
            get => _repo.UIStates.IsAuthorColumnVisibleInHistory;
            set
            {
                if (_repo.UIStates.IsAuthorColumnVisibleInHistory != value)
                {
                    _repo.UIStates.IsAuthorColumnVisibleInHistory = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool IsSHAColumnVisible
        {
            get => _repo.UIStates.IsSHAColumnVisibleInHistory;
            set
            {
                if (_repo.UIStates.IsSHAColumnVisibleInHistory != value)
                {
                    _repo.UIStates.IsSHAColumnVisibleInHistory = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool IsDateTimeColumnVisible
        {
            get => _repo.UIStates.IsDateTimeColumnVisibleInHistory;
            set
            {
                if (_repo.UIStates.IsDateTimeColumnVisibleInHistory != value)
                {
                    _repo.UIStates.IsDateTimeColumnVisibleInHistory = value;
                    OnPropertyChanged();
                }
            }
        }

        public List<string> SoloTargets
        {
            get => _soloTargets;
            set
            {
                if (SetProperty(ref _soloTargets, value))
                    UpdateDisplayCommits();
            }
        }

        public List<Models.Commit> Commits
        {
            get => _commits;
            set
            {
                _rawCommits = value;
                UpdateDisplayCommits();
            }
        }

        public void UpdateDisplayCommits()
        {
            var processed = _rawCommits;

            // AST-driven token filter via QueryParser + ExprEvaluator
            if (SearchTokens.Count > 0)
            {
                var spec = Controls.QueryParser.Parse(SearchTokens, SearchProviders);

                if (spec.HasSubGroups)
                {
                    // Multiple parenthesized groups: each is AND-ed internally, OR-ed across groups
                    processed = null;
                    foreach (var sub in spec.SubGroups)
                    {
                        var r = ApplyGroupFilters(_rawCommits, sub);
                        ApplyFallbackTerms(sub, ref r);
                        processed = processed == null ? r : processed.Union(r).DistinctBy(c => c.SHA).ToList();
                    }
                }
                else
                {
                    // Single group: existing behavior
                    processed = ApplyGroupFilters(_rawCommits, spec);
                    ApplyFallbackTerms(spec, ref processed);
                }

                _suggestionCache = _cacheManager.Compute(_rawCommits, _commits, spec);
            }
            else
            {
                // No tokens: all caches = _rawCommits
                _suggestionCache = new Dictionary<string, List<Models.Commit>>();
                foreach (var provider in SearchProviders)
                {
                    if (provider.Prefix is "sort:" or "gitlog:")
                        continue;
                    _suggestionCache[provider.Prefix] = _rawCommits;
                }
            }

            processed = FilterCommits(processed, _soloTargets);
            processed = FoldCommits(processed);
            if (SetProperty(ref _commits, processed, nameof(Commits)))
            {
                try
                {
                    _suppressGraphRefreshFromSelectionChange = true;
                    PostCommitsChanged();
                }
                finally
                {
                    _suppressGraphRefreshFromSelectionChange = false;
                }

                GenerateGraph(_commits, true);
            }
            else
            {
                GenerateGraph(_commits);
            }
        }

        public Models.CommitGraph Graph
        {
            get => _graph;
            set => SetProperty(ref _graph, value);
        }

        public long HoveredCommitIndex
        {
            get => _hoveredCommitIndex;
            set
            {
                if (SetProperty(ref _hoveredCommitIndex, value))
                    RefreshHoveredLineage();
            }
        }

        public bool[] HoveredLineageCommits
        {
            get => _hoveredLineageCommits;
            set => SetProperty(ref _hoveredLineageCommits, value);
        }

        public Models.CommitLineageSearchMethod LineageSearchMethod
        {
            get => _repo.UIStates.LineageSearchMethod;
            set
            {
                if (_repo.UIStates.LineageSearchMethod != value)
                {
                    _repo.UIStates.LineageSearchMethod = value;
                    OnPropertyChanged();

                    RefreshHoveredLineage();

                    var highlightSelected = _repo.UIStates.GraphHighlighting == Models.CommitGraphHighlighting.SelectedCommitsOnly ||
                                            _repo.UIStates.GraphHighlighting == Models.CommitGraphHighlighting.CurrentBranchAndSelectedCommits;
                    if (highlightSelected && _selectedCommits.Count == 1)
                        CalculateTargetLineage(_selectedCommits[0]);
                    else
                        GenerateGraph(_commits);
                }
            }
        }

        public Models.CommitGraphHighlighting GraphHighlighting
        {
            get => _repo.UIStates.GraphHighlighting;
            set
            {
                if (_repo.UIStates.GraphHighlighting != value)
                {
                    _repo.UIStates.GraphHighlighting = value;
                    GenerateGraph(_commits);
                }
            }
        }

        public List<Models.Commit> SelectedCommits
        {
            get => _selectedCommits;
            set
            {
                var oldCount = _selectedCommits.Count;
                if (SetProperty(ref _selectedCommits, value) && oldCount + value.Count > 0)
                    PostSelectedCommitsChanged();
            }
        }

        public HashSet<int> SelectedLineagePaths
        {
            get => _selectedLineagePaths;
            set => SetProperty(ref _selectedLineagePaths, value);
        }

        public bool[] SelectedLineageCommits
        {
            get => _selectedLineageCommits;
            set => SetProperty(ref _selectedLineageCommits, value);
        }

        public object DetailContext
        {
            get => _detailContext;
            set
            {
                if (SetProperty(ref _detailContext, value))
                    OnPropertyChanged(nameof(IsOpenAsStandaloneVisible));
            }
        }

        public Models.Bisect Bisect
        {
            get => _bisect;
            private set => SetProperty(ref _bisect, value);
        }

        public Models.Branch CurrentBranch
        {
            get => _repo.CurrentBranch;
        }

        public AvaloniaList<Models.IssueTracker> IssueTrackers
        {
            get => _repo.IssueTrackers;
        }

        public GridLength LeftArea
        {
            get => _leftArea;
            set => SetProperty(ref _leftArea, value);
        }

        public GridLength RightArea
        {
            get => _rightArea;
            set => SetProperty(ref _rightArea, value);
        }

        public GridLength TopArea
        {
            get => _isMaximizeDetails ? new GridLength(0, GridUnitType.Pixel) : _topArea;
            set
            {
                if (!Preferences.Instance.UseTwoColumnsLayoutInHistories && !_isMaximizeDetails)
                    SetProperty(ref _topArea, value);
            }
        }

        public GridLength BottomArea
        {
            get => _isCollapseDetails ?
                    new GridLength(28, GridUnitType.Pixel) :
                    (_isMaximizeDetails ? new GridLength(1, GridUnitType.Star) : _bottomArea);
            set
            {
                if (!Preferences.Instance.UseTwoColumnsLayoutInHistories
                    && !_isCollapseDetails && !_isMaximizeDetails)
                    SetProperty(ref _bottomArea, value);
            }
        }

        public double AuthorColumnWidth
        {
            get => _repo.UIStates.AuthorColumnWidth;
            set => _repo.UIStates.AuthorColumnWidth = value;
        }

        public bool IsOpenAsStandaloneVisible
        {
            get => DetailContext is CommitDetail or RevisionCompare;
        }

        public bool IsCollapseDetails
        {
            get => _isCollapseDetails;
            set
            {
                if (!Preferences.Instance.UseTwoColumnsLayoutInHistories && SetProperty(ref _isCollapseDetails, value))
                {
                    if (value && _isMaximizeDetails)
                        SetProperty(ref _isMaximizeDetails, false, nameof(IsMaximizeDetails));

                    OnPropertyChanged(nameof(TopArea));
                    OnPropertyChanged(nameof(BottomArea));
                }
            }
        }

        public bool IsMaximizeDetails
        {
            get => _isMaximizeDetails;
            set
            {
                if (!Preferences.Instance.UseTwoColumnsLayoutInHistories && SetProperty(ref _isMaximizeDetails, value))
                {
                    if (value && _isCollapseDetails)
                        SetProperty(ref _isCollapseDetails, false, nameof(IsCollapseDetails));

                    OnPropertyChanged(nameof(TopArea));
                    OnPropertyChanged(nameof(BottomArea));
                }
            }
        }

        public Histories(Repository repo)
        {
            _repo = repo;
            _commitDetailSharedData = new CommitDetailSharedData();

            static List<string> BuildGitOptionTokens(Models.HistoryShowFlags flags)
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

            void SyncGitTokensFromFlags(Models.HistoryShowFlags flags)
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

            _repo.UIStates.HistoryFilters.CollectionChanged += (_, e) =>
            {
                if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Reset)
                    _searchDrivenFilters.Clear();
                UpdateDisplayCommits();
            };

            _repo.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName != nameof(Repository.HistoryShowFlags))
                    return;

                if (_syncingRepoFlagsFromTokens)
                    return;

                SyncGitTokensFromFlags(_repo.HistoryShowFlags);
            };

            Preferences.Instance.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(Preferences.EnableLinearCommitFolding))
                    UpdateDisplayCommits();
            };

            Func<string, System.Threading.CancellationToken, Task<IEnumerable<Controls.TokenSuggestion>>> authorSuggester = (pattern, ct) =>
            {
                var source = _suggestionCache.GetValueOrDefault("a:") ?? _commits;
                var authors = source.Select(c => c.Author).DistinctBy(a => a.Name);
                var suggestions = authors
                    .Where(a => string.IsNullOrEmpty(pattern) || a.Name.Contains(pattern, StringComparison.OrdinalIgnoreCase) || a.Email.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                    .Select(a => new Controls.TokenSuggestion { Name = a.Name, Description = a.Email });
                return Task.FromResult(suggestions);
            };

            Func<string, System.Threading.CancellationToken, Task<IEnumerable<Controls.TokenSuggestion>>> branchSuggester = (pattern, ct) =>
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
                    .Select(n => new Controls.TokenSuggestion { Name = n });
                return Task.FromResult(suggestions);
            };

            Func<string, System.Threading.CancellationToken, Task<IEnumerable<Controls.TokenSuggestion>>> remoteSuggester = (pattern, ct) =>
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
                    .Select(n => new Controls.TokenSuggestion { Name = n, Description = "所有远程分支" });

                // Suggest specific remote branch names (like "G/H")
                var branchSuggestions = remoteBranches
                    .Where(n => string.IsNullOrEmpty(pattern) || n.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(n => n)
                    .Take(100)
                    .Select(n => new Controls.TokenSuggestion { Name = n });

                var suggestions = branchSuggestions.ToList();
                foreach (var rn in remoteNames)
                {
                    if (string.IsNullOrEmpty(pattern) || rn.Name.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                    {
                        if (!suggestions.Any(s => s.Name == rn.Name))
                            suggestions.Insert(0, rn);
                    }
                }

                return Task.FromResult((IEnumerable<Controls.TokenSuggestion>)suggestions);
            };

            Func<string, System.Threading.CancellationToken, Task<IEnumerable<Controls.TokenSuggestion>>> tagSuggester = (pattern, ct) =>
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
                    .Select(n => new Controls.TokenSuggestion { Name = n });
                return Task.FromResult(suggestions);
            };

            Func<string, System.Threading.CancellationToken, Task<IEnumerable<Controls.TokenSuggestion>>> messageSuggester = (pattern, ct) =>
            {
                var messageSource = _suggestionCache.GetValueOrDefault("m:") ?? _commits;
                var subjects = messageSource
                    .Select(c => c.Subject)
                    .Where(s => !string.IsNullOrEmpty(s))
                    .Distinct(StringComparer.OrdinalIgnoreCase);

                var suggestions = subjects
                    .Where(s => string.IsNullOrEmpty(pattern) || s.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                    .Take(100)
                    .Select(s => new Controls.TokenSuggestion { Name = s });
                return Task.FromResult(suggestions);
            };

            Func<string, System.Threading.CancellationToken, Task<IEnumerable<Controls.TokenSuggestion>>> soloCommitSuggester = (pattern, ct) =>
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
                    .Select(c => new Controls.TokenSuggestion
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
                        new Controls.TokenSuggestion
                        {
                            Name = "HEAD",
                            Value = "HEAD",
                            Description = string.IsNullOrWhiteSpace(head.Subject) ? "当前分支头提交" : head.Subject,
                        }
                    }.Concat(commits);
                }

                return Task.FromResult(commits.DistinctBy(c => c.Value ?? c.Name));
            };

            var implementedIcon = "M 1.5 6.5 L 4.5 9.5 L 10.5 2.5";


            var groupFilters = new Controls.TokenSuggestionGroup("filters", "常规过滤");
            var groupAdvanced = new Controls.TokenSuggestionGroup("advanced", "高级检索");
            var groupView = new Controls.TokenSuggestionGroup("view", "视图控制");
            var groupGit = new Controls.TokenSuggestionGroup("git", "Git 选项");

            var isProv = new[] {
                "merged", "unmerged", "tag",
                "branch", "merge", "cherrypick",
                "head", "folded" };
            SearchProviders.Add(new Controls.StaticTokenSuggestionProvider("is:", "状态过滤",
            groupAdvanced, isProv, Controls.TokenLogicMode.AutoAnd, icon: implementedIcon, priority: 11));
            SearchProviders.Add(new Controls.StaticTokenSuggestionProvider("a:", "作者", groupFilters, suggester: authorSuggester, logicMode: Controls.TokenLogicMode.AutoOr, alias: new[] { "author:" }, icon: implementedIcon, priority: 31));
            SearchProviders.Add(new Controls.StaticTokenSuggestionProvider("m:", "提交消息", groupFilters, suggester: messageSuggester, logicMode: Controls.TokenLogicMode.AutoOr, alias: new[] { "message:" }, icon: implementedIcon, priority: 101));
            SearchProviders.Add(new Controls.StaticTokenSuggestionProvider("b:", "分支", groupFilters, suggester: branchSuggester, logicMode: Controls.TokenLogicMode.AutoOr, alias: new[] { "branch:" }, icon: implementedIcon, isPersistent: true, priority: 0));
            SearchProviders.Add(new Controls.StaticTokenSuggestionProvider("solo:", "Solo 提交链过滤", groupView,
                suggester: soloCommitSuggester, logicMode: Controls.TokenLogicMode.AutoOr,
                alias: new[] { "sole:" }, icon: implementedIcon, priority: 1));
            SearchProviders.Add(new Controls.StaticTokenSuggestionProvider("t:", "标签", groupFilters, suggester: tagSuggester, alias: new[] { "tag:" }, icon: implementedIcon, isPersistent: true, priority: 0));
            SearchProviders.Add(new Controls.StaticTokenSuggestionProvider("r:", "远程分支", groupFilters, suggester: remoteSuggester, alias: new[] { "remote:" }, icon: implementedIcon, isPersistent: true, priority: 0));
            SearchProviders.Add(new Controls.StaticTokenSuggestionProvider("f:", "文件路径", groupFilters, alias: new[] { "file:" }, priority: 41));
            SearchProviders.Add(new Controls.StaticTokenSuggestionProvider("p:", "路径", groupFilters, alias: new[] { "path:" }, priority: 41));
            SearchProviders.Add(new Controls.StaticTokenSuggestionProvider("s:", "哈希", groupFilters, alias: new[] { "sha:" }, icon: implementedIcon, priority: 41));
            SearchProviders.Add(new Controls.StaticTokenSuggestionProvider("since:", "起始时间", groupFilters, alias: new[] { "after:" }, icon: implementedIcon, priority: 51));
            SearchProviders.Add(new Controls.StaticTokenSuggestionProvider("until:", "结束时间", groupFilters, alias: new[] { "before:" }, icon: implementedIcon, priority: 51));

            SearchProviders.Add(new Controls.StaticTokenSuggestionProvider("c:", "提交者", groupAdvanced, alias: new[] { "committer:" }, icon: implementedIcon, priority: 31));
            SearchProviders.Add(new Controls.StaticTokenSuggestionProvider("e:", "邮箱", groupAdvanced, alias: new[] { "email:" }, icon: implementedIcon, priority: 31));
            SearchProviders.Add(new Controls.StaticTokenSuggestionProvider("S:", "内容搜索 (Pickaxe)", groupAdvanced, priority: 61));
            SearchProviders.Add(new Controls.StaticTokenSuggestionProvider("G:", "正则搜索 (Grep)", groupAdvanced, priority: 61));
            SearchProviders.Add(new Controls.StaticTokenSuggestionProvider("change:", "变更类型", groupAdvanced, priority: 61));
            SearchProviders.Add(new Controls.StaticTokenSuggestionProvider("signed:", "GPG 签名状态", groupAdvanced, priority: 61));
            SearchProviders.Add(new Controls.StaticTokenSuggestionProvider("parent:", "父提交搜索", groupAdvanced, priority: 61));

            SearchProviders.Add(new Controls.StaticTokenSuggestionProvider("sort:", "排序方式", groupView, new[] { "Commit Date", "Topologically" }, Controls.TokenLogicMode.SingleReplace, priority: 999));

            SearchProviders.Add(new Controls.StaticTokenSuggestionProvider("gitlog:", "git 解析选项", groupGit, new[] { "reflog", "1st-p", "decora" }, Controls.TokenLogicMode.AutoOr, isPersistent: true, priority: 0));

            _cacheManager = new SuggestionCacheManager(SearchProviders, (prefix, c, val) => EvalTerm(prefix, c, val));

            bool ToggleColumnByName(string name)
            {
                if (name.Equals("author", StringComparison.OrdinalIgnoreCase))
                {
                    IsAuthorColumnVisible = !IsAuthorColumnVisible;
                    return true;
                }

                if (name.Equals("sha", StringComparison.OrdinalIgnoreCase))
                {
                    IsSHAColumnVisible = !IsSHAColumnVisible;
                    return true;
                }

                if (name.Equals("time", StringComparison.OrdinalIgnoreCase) || name.Equals("datetime", StringComparison.OrdinalIgnoreCase))
                {
                    IsDateTimeColumnVisible = !IsDateTimeColumnVisible;
                    return true;
                }

                return false;
            }

            bool SetColumnByName(string name, bool value)
            {
                if (name.Equals("author", StringComparison.OrdinalIgnoreCase))
                {
                    IsAuthorColumnVisible = value;
                    return true;
                }

                if (name.Equals("sha", StringComparison.OrdinalIgnoreCase))
                {
                    IsSHAColumnVisible = value;
                    return true;
                }

                if (name.Equals("time", StringComparison.OrdinalIgnoreCase) || name.Equals("datetime", StringComparison.OrdinalIgnoreCase))
                {
                    IsDateTimeColumnVisible = value;
                    return true;
                }

                return false;
            }

            static bool IsKnownUiField(string name)
            {
                return name.Equals("author", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("sha", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("time", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("datetime", StringComparison.OrdinalIgnoreCase);
            }

            IEnumerable<Controls.TokenSuggestion> SuggestUiSlashArguments(Controls.TokenSlashSuggestionContext ctx)
            {
                static IEnumerable<Controls.TokenSuggestion> BuildFieldSuggestions(string pattern)
                {
                    var fields = new[]
                    {
                        new Controls.TokenSuggestion { Name = "author", Description = "作者列" },
                        new Controls.TokenSuggestion { Name = "sha", Description = "SHA 列" },
                        new Controls.TokenSuggestion { Name = "time", Description = "时间列" },
                    };

                    if (string.IsNullOrWhiteSpace(pattern))
                        return fields;

                    return fields.Where(x => x.Name.Contains(pattern, StringComparison.OrdinalIgnoreCase));
                }

                static IEnumerable<Controls.TokenSuggestion> BuildValueSuggestions(string field, string pattern)
                {
                    var values = new[]
                    {
                        new Controls.TokenSuggestion { Name = $"{field} true", Description = "显式开启" },
                        new Controls.TokenSuggestion { Name = $"{field} false", Description = "显式关闭" },
                        new Controls.TokenSuggestion { Name = $"{field} toggle", Description = "切换" },
                    };

                    if (string.IsNullOrWhiteSpace(pattern))
                        return values;

                    return values.Where(x => x.Name.Contains(pattern, StringComparison.OrdinalIgnoreCase));
                }

                var tokens = ctx.ArgumentTokens ?? [];
                var active = ctx.ActiveToken ?? string.Empty;

                if (ctx.ActiveTokenIndex <= 0)
                    return BuildFieldSuggestions(active);

                var field = tokens.Count > 0 ? tokens[0] : string.Empty;
                if (string.IsNullOrWhiteSpace(field) || !IsKnownUiField(field))
                    return BuildFieldSuggestions(field);

                return BuildValueSuggestions(field, active);
            }

            bool ExecuteUiSlashCommand(Controls.TokenSlashExecuteContext ctx)
            {
                var tokens = ctx.ArgumentTokens ?? [];
                if (tokens.Count == 0)
                    return false;

                var field = tokens[0];
                if (tokens.Count == 1)
                    return ToggleColumnByName(field);

                var mode = tokens[1].Trim().ToLowerInvariant();
                return mode switch
                {
                    "true" or "on" or "1" => SetColumnByName(field, true),
                    "false" or "off" or "0" => SetColumnByName(field, false),
                    "toggle" => ToggleColumnByName(field),
                    _ => false,
                };
            }

            SearchSlashCommands.Add(new Controls.TokenSlashCommand
            {
                Name = "ui",
                Description = "视图控制：/ui <author|sha|time> [true|false|toggle]",
                Icon = implementedIcon,
                RequiresArgument = true,
                Suggest = SuggestUiSlashArguments,
                Execute = ExecuteUiSlashCommand,
            });
            SearchSlashCommands.Add(new Controls.TokenSlashCommand
            {
                Name = "st",
                Description = "设置快捷命令：/st <author|sha|time> [true|false|toggle]",
                Icon = implementedIcon,
                RequiresArgument = true,
                Suggest = SuggestUiSlashArguments,
                Execute = ExecuteUiSlashCommand,
            });

            IEnumerable<Controls.TokenSuggestion> SuggestGotoArguments(Controls.TokenSlashSuggestionContext ctx)
            {
                var tokens = ctx.ArgumentTokens;
                var endsWithSpace = ctx.EndsWithWhitespace;
                var active = ctx.ActiveToken ?? string.Empty;

                // No sub-command yet: suggest sub-commands
                if (tokens.Count == 0 || (tokens.Count == 1 && !endsWithSpace))
                {
                    var subCommands = new[] { "sha", "tag", "branch", "head", "commit", "solo" };
                    foreach (var cmd in subCommands)
                    {
                        if (!string.IsNullOrEmpty(active) && !cmd.StartsWith(active, StringComparison.OrdinalIgnoreCase))
                            continue;
                        var syntax = cmd switch
                        {
                            "sha" => "/goto sha <SHA>",
                            "tag" => "/goto tag <标签名>",
                            "branch" => "/goto branch <分支名>",
                            "head" => "/goto head",
                            "commit" => "/goto commit <关键词>",
                            "solo" => "/goto solo <SHA>",
                            _ => "",
                        };
                        yield return new Controls.TokenSuggestion { Name = cmd, Description = syntax };
                    }
                    yield break;
                }

                // Has sub-command: suggest values
                var subCmd = tokens[0].ToLowerInvariant();
                switch (subCmd)
                {
                    case "head":
                        yield break;

                    case "branch":
                        var branchNames = (_commits ?? [])
                            .SelectMany(c => c.Decorators)
                            .Where(d => d.Type is Models.DecoratorType.LocalBranchHead or Models.DecoratorType.CurrentBranchHead)
                            .Select(d => d.Name)
                            .Where(n => !string.IsNullOrEmpty(n))
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .Where(n => n.Contains(active, StringComparison.OrdinalIgnoreCase))
                            .OrderBy(n => n)
                            .Take(20);
                        foreach (var name in branchNames)
                            yield return new Controls.TokenSuggestion { Name = $"branch {name}", Description = "分支" };
                        break;

                    case "tag":
                        var tagNames = (_commits ?? [])
                            .SelectMany(c => c.Decorators)
                            .Where(d => d.Type == Models.DecoratorType.Tag)
                            .Select(d => d.Name)
                            .Where(n => !string.IsNullOrEmpty(n))
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .Where(n => n.Contains(active, StringComparison.OrdinalIgnoreCase))
                            .OrderBy(n => n)
                            .Take(20);
                        foreach (var name in tagNames)
                            yield return new Controls.TokenSuggestion { Name = $"tag {name}", Description = "标签" };
                        break;

                    case "sha":
                        var shaMatches = (_commits ?? [])
                            .Where(c => !string.IsNullOrWhiteSpace(c?.SHA) && c.SHA.StartsWith(active, StringComparison.OrdinalIgnoreCase))
                            .Take(10)
                            .Select(c => new Controls.TokenSuggestion
                            {
                                Name = $"sha {c.SHA[..Math.Min(10, c.SHA.Length)]}",
                                Description = $"[SHA] {c.Subject ?? ""} · {c.Author.Name}",
                            });
                        foreach (var s in shaMatches)
                            yield return s;
                        break;

                    case "solo":
                        var soloTokens = SearchTokens
                            .Where(t => t.StartsWith("solo:", StringComparison.OrdinalIgnoreCase))
                            .Select(t => t["solo:".Length..])
                            .Where(s => !string.IsNullOrEmpty(s) && (string.IsNullOrEmpty(active) || s.StartsWith(active, StringComparison.OrdinalIgnoreCase)))
                            .Take(10);
                        foreach (var sha in soloTokens)
                            yield return new Controls.TokenSuggestion { Name = $"solo {sha}", Description = $"[Solo] {sha}" };
                        break;

                    case "commit":
                        var msgMatches = (_commits ?? [])
                            .Where(c => !string.IsNullOrWhiteSpace(c?.SHA) && !string.IsNullOrEmpty(c.Subject) && c.Subject.Contains(active, StringComparison.OrdinalIgnoreCase))
                            .Take(10)
                            .Select(c => new Controls.TokenSuggestion
                            {
                                Name = $"commit {c.SHA[..Math.Min(10, c.SHA.Length)]}",
                                Description = $"[提交信息] {c.Subject ?? ""} · {c.Author.Name}",
                            });
                        foreach (var s in msgMatches)
                            yield return s;
                        break;
                }
            }

            bool ExecuteGotoCommand(Controls.TokenSlashExecuteContext ctx)
            {
                var tokens = ctx.ArgumentTokens?.ToList() ?? [];
                if (tokens.Count == 0)
                    return false;

                var subCmd = tokens[0].Trim().ToLowerInvariant();
                switch (subCmd)
                {
                    case "head":
                        _repo.NavigateToCommit("HEAD");
                        return true;

                    case "branch":
                        if (tokens.Count < 2)
                            return false;
                        _repo.NavigateToBranch(tokens[1].Trim());
                        return true;

                    case "tag":
                        if (tokens.Count < 2)
                            return false;
                        _repo.NavigateToTag(string.Join(" ", tokens.Skip(1)).Trim());
                        return true;

                    case "sha":
                        if (tokens.Count < 2)
                            return false;
                        _repo.NavigateToCommit(tokens[1].Trim());
                        return true;

                    case "solo":
                        if (tokens.Count < 2)
                            return false;
                        _repo.NavigateToCommit(tokens[1].Trim());
                        return true;

                    case "commit":
                        if (tokens.Count < 2)
                            return false;
                        var query = string.Join(" ", tokens.Skip(1)).Trim().ToLowerInvariant();
                        var match = (_commits ?? [])
                            .FirstOrDefault(c => c.Subject != null &&
                                c.Subject.Contains(query, StringComparison.OrdinalIgnoreCase));
                        if (match == null)
                            return false;
                        _repo.NavigateToCommit(match.SHA);
                        return true;

                    default:
                        return false;
                }
            }

            SearchSlashCommands.Add(new Controls.TokenSlashCommand
            {
                Name = "goto",
                Description = "跳转：/goto <sha|tag|branch|head|commit|solo> [参数]",
                Icon = "M 1.5 6.5 L 4.5 9.5 L 10.5 2.5",
                RequiresArgument = true,
                Suggest = SuggestGotoArguments,
                Execute = ExecuteGotoCommand,
            });

            SearchTokens.CollectionChanged += (_, e) =>
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
            };

            // Initial sync: reflect persisted HistoryShowFlags into gitlog:* tokens.
            SyncGitTokensFromFlags(_repo.HistoryShowFlags);
        }

        public void SetVisibleCommitRange(int top, int bottom)
        {
            _visibleTopIndex = top;
            _visibleBottomIndex = bottom;
        }

        public void NotifyCurrentBranchChanged()
        {
            OnPropertyChanged(nameof(CurrentBranch));
        }

        public Models.BisectState UpdateBisectInfo()
        {
            var test = Path.Combine(_repo.GitDir, "BISECT_START");
            if (!File.Exists(test))
            {
                Bisect = null;
                return Models.BisectState.None;
            }

            var info = new Models.Bisect();
            var dir = Path.Combine(_repo.GitDir, "refs", "bisect");
            if (Directory.Exists(dir))
            {
                var files = new DirectoryInfo(dir).GetFiles();
                foreach (var file in files)
                {
                    if (file.Name.StartsWith("bad"))
                        info.Bads.Add(File.ReadAllText(file.FullName).Trim());
                    else if (file.Name.StartsWith("good"))
                        info.Goods.Add(File.ReadAllText(file.FullName).Trim());
                }
            }

            Bisect = info;

            if (info.Bads.Count == 0 || info.Goods.Count == 0)
                return Models.BisectState.WaitingForRange;
            else
                return Models.BisectState.Detecting;
        }

        public void NavigateTo(string commitSHA)
        {
            var commit = _commits.Find(x => x.SHA.StartsWith(commitSHA, StringComparison.Ordinal));
            if (commit != null)
            {
                SelectedCommits = [commit];
                return;
            }

            Task.Run(async () =>
            {
                var c = await new Commands.QuerySingleCommit(_repo.FullPath, commitSHA)
                    .GetResultAsync()
                    .ConfigureAwait(false);

                Dispatcher.UIThread.Post(() =>
                {
                    _ignoreSelectionChange = true;
                    SelectedCommits = [];

                    if (_detailContext is CommitDetail detail)
                    {
                        detail.Commit = c;
                    }
                    else
                    {
                        var commitDetail = new CommitDetail(_repo, _commitDetailSharedData);
                        commitDetail.Commit = c;
                        DetailContext = commitDetail;
                    }

                    _ignoreSelectionChange = false;
                });
            });
        }

        public async Task<Models.Commit> GetCommitAsync(string sha)
        {
            return await new Commands.QuerySingleCommit(_repo.FullPath, sha)
                .GetResultAsync()
                .ConfigureAwait(false);
        }

        public async Task<bool> CheckoutBranchByDecoratorAsync(Models.Decorator decorator)
        {
            if (decorator == null)
                return false;

            if (decorator.Type == Models.DecoratorType.CurrentBranchHead ||
                decorator.Type == Models.DecoratorType.CurrentCommitHead)
                return true;

            if (decorator.Type == Models.DecoratorType.LocalBranchHead)
            {
                var b = _repo.Branches.Find(x => x.Name == decorator.Name);
                if (b == null)
                    return false;

                await _repo.CheckoutBranchAsync(b);
                return true;
            }

            if (decorator.Type == Models.DecoratorType.RemoteBranchHead)
            {
                var rb = _repo.Branches.Find(x => x.FriendlyName == decorator.Name);
                if (rb == null)
                    return false;

                var lb = _repo.Branches.Find(x => x.IsLocal && x.Upstream == rb.FullName);
                if (lb == null || lb.Ahead.Count > 0)
                {
                    if (_repo.CanCreatePopup())
                        _repo.ShowPopup(new CreateBranch(_repo, rb));
                }
                else if (lb.Behind.Count > 0)
                {
                    if (_repo.CanCreatePopup())
                        _repo.ShowPopup(new CheckoutAndFastForward(_repo, lb, rb));
                }
                else if (!lb.IsCurrent)
                {
                    await _repo.CheckoutBranchAsync(lb);
                }

                return true;
            }

            return false;
        }

        public async Task CheckoutBranchByCommitAsync(Models.Commit commit)
        {
            if (commit.IsCurrentHead)
                return;

            Models.Branch firstRemoteBranch = null;
            foreach (var d in commit.Decorators)
            {
                if (d.Type == Models.DecoratorType.LocalBranchHead)
                {
                    var b = _repo.Branches.Find(x => x.Name == d.Name);
                    if (b == null)
                        continue;

                    await _repo.CheckoutBranchAsync(b);
                    return;
                }

                if (d.Type == Models.DecoratorType.RemoteBranchHead)
                {
                    var rb = _repo.Branches.Find(x => x.FriendlyName == d.Name);
                    if (rb == null)
                        continue;

                    var lb = _repo.Branches.Find(x => x.IsLocal && x.Upstream == rb.FullName);
                    if (lb != null && lb.Behind.Count > 0 && lb.Ahead.Count == 0)
                    {
                        if (_repo.CanCreatePopup())
                            _repo.ShowPopup(new CheckoutAndFastForward(_repo, lb, rb));
                        return;
                    }

                    firstRemoteBranch ??= rb;
                }
            }

            if (_repo.CanCreatePopup())
            {
                if (firstRemoteBranch != null)
                    _repo.ShowPopup(new CreateBranch(_repo, firstRemoteBranch));
                else if (!_repo.IsBare)
                    _repo.ShowPopup(new CheckoutCommit(_repo, commit));
            }
        }

        public async Task CherryPickAsync(Models.Commit commit)
        {
            if (_repo.CanCreatePopup())
            {
                if (commit.Parents.Count <= 1)
                {
                    _repo.ShowPopup(new CherryPick(_repo, [commit]));
                }
                else
                {
                    var parents = new List<Models.Commit>();
                    foreach (var sha in commit.Parents)
                    {
                        var parent = _commits.Find(x => x.SHA.Equals(sha, StringComparison.Ordinal));
                        if (parent == null)
                            parent = await new Commands.QuerySingleCommit(_repo.FullPath, sha).GetResultAsync();

                        if (parent != null)
                            parents.Add(parent);
                    }

                    _repo.ShowPopup(new CherryPick(_repo, commit, parents));
                }
            }
        }

        public async Task<string> GetCommitFullMessageAsync(Models.Commit commit)
        {
            return await new Commands.QueryCommitFullMessage(_repo.FullPath, commit.SHA)
                .GetResultAsync()
                .ConfigureAwait(false);
        }

        public async Task<Models.Commit> CompareWithHeadAsync(Models.Commit commit)
        {
            var head = _commits.Find(x => x.IsCurrentHead);
            if (head == null)
            {
                _repo.SearchCommitContext.Selected = null;
                head = await new Commands.QuerySingleCommit(_repo.FullPath, "HEAD").GetResultAsync();
                if (head != null)
                    DetailContext = new RevisionCompare(_repo, commit, head);

                return null;
            }

            return head;
        }

        public void CompareWithWorktree(Models.Commit commit)
        {
            DetailContext = new RevisionCompare(_repo, commit, null);
        }

        private void PostCommitsChanged()
        {
            _commitMap.Clear();
            for (int i = 0; i < _commits.Count; i++)
            {
                var c = _commits[i];
                c.Index = i;
                _commitMap[c.SHA] = c;
            }

            if (_selectedCommits.Count == 0)
                return;

            if (_commits.Count == 0 || _selectedCommits.Count > 20)
            {
                SelectedCommits = [];
                return;
            }

            var set = new HashSet<string>();
            foreach (var c in _selectedCommits)
                set.Add(c.SHA);

            var selected = new List<Models.Commit>();
            foreach (var c in _commits)
            {
                if (set.Contains(c.SHA))
                {
                    selected.Add(c);
                    set.Remove(c.SHA);
                    if (set.Count == 0)
                        break;
                }
            }

            SelectedCommits = selected;
        }

        private void CalculateTargetLineage(Models.Commit commit)
        {
            var requestVersion = Interlocked.Increment(ref _lineageRequestVersion);

            Task.Run(() =>
            {
                if (commit == null)
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        if (requestVersion != _lineageRequestVersion)
                            return;

                        SelectedLineageCommits = null;
                        SelectedLineagePaths = null;
                    });
                    return;
                }

                var paths = new HashSet<int>();
                var lineage = Models.CommitGraph.GetCommitLineageFast(_commits, _commitMap, commit, LineageSearchMethod, 20000);
                for (int i = 0; i < lineage.Length; i++)
                {
                    if (lineage[i])
                    {
                        var c = _commits[i];
                        if (c.PathIndex >= 0)
                            paths.Add(c.PathIndex);
                    }
                }

                Dispatcher.UIThread.Post(() =>
                {
                    if (requestVersion != _lineageRequestVersion)
                        return;

                    if (_selectedCommits.Count != 1 || !_selectedCommits[0].SHA.Equals(commit.SHA, StringComparison.Ordinal))
                        return;

                    SelectedLineageCommits = lineage;
                    SelectedLineagePaths = paths;
                    GenerateGraph(_commits);
                });
            });
        }

        private void PostSelectedCommitsChanged()
        {
            if (_ignoreSelectionChange)
                return;

            var deferGraphRefresh = false;

            if (_selectedCommits.Count == 0)
            {
                _repo.SearchCommitContext.Selected = null;
                DetailContext = new Models.Null();
                SelectedLineageCommits = null;
                SelectedLineagePaths = null;
            }
            else if (_selectedCommits.Count == 1)
            {
                var c = _selectedCommits[0];
                if (_repo.SearchCommitContext.Selected == null || !_repo.SearchCommitContext.Selected.SHA.Equals(c.SHA, StringComparison.Ordinal))
                    _repo.SearchCommitContext.Selected = _repo.SearchCommitContext.Results?.Find(x => x.SHA.Equals(c.SHA, StringComparison.Ordinal));

                if (_detailContext is CommitDetail detail)
                    detail.Commit = c;
                else
                    DetailContext = new CommitDetail(_repo, _commitDetailSharedData) { Commit = c };

                var highlightSelected = _repo.UIStates.GraphHighlighting == Models.CommitGraphHighlighting.SelectedCommitsOnly ||
                                         _repo.UIStates.GraphHighlighting == Models.CommitGraphHighlighting.CurrentBranchAndSelectedCommits;
                if (highlightSelected)
                {
                    CalculateTargetLineage(c);
                    deferGraphRefresh = true;
                }
            }
            else if (_selectedCommits.Count == 2)
            {
                _repo.SearchCommitContext.Selected = null;

                if (_detailContext is RevisionCompare compare)
                    compare.SetTargets(_selectedCommits[1], _selectedCommits[0]);
                else
                    DetailContext = new RevisionCompare(_repo, _selectedCommits[1], _selectedCommits[0]);

                SelectedLineageCommits = null;
                SelectedLineagePaths = null;
            }
            else
            {
                _repo.SearchCommitContext.Selected = null;
                DetailContext = new Models.Count(_selectedCommits.Count);
                SelectedLineageCommits = null;
                SelectedLineagePaths = null;
            }

            if (_suppressGraphRefreshFromSelectionChange || deferGraphRefresh)
                return;

            if (_repo.UIStates.GraphHighlighting >= Models.CommitGraphHighlighting.SelectedCommitsOnly)
                GenerateGraph(_commits);
        }

        private void GenerateGraph(List<Models.Commit> commits, bool commitsChanged = false)
        {
            var firstParentOnly = _repo.UIStates.HistoryShowFlags.HasFlag(Models.HistoryShowFlags.FirstParentOnly);
            var highlighting = _repo.UIStates.GraphHighlighting;

            bool[] selectedLineage = null;
            if (highlighting >= Models.CommitGraphHighlighting.SelectedCommitsOnly)
            {
                if (_selectedLineageCommits != null)
                {
                    selectedLineage = _selectedLineageCommits;
                }
                else if (_selectedCommits.Count > 0)
                {
                    selectedLineage = new bool[commits.Count];
                    foreach (var c in _selectedCommits)
                    {
                        if (c.Index >= 0 && c.Index < selectedLineage.Length)
                            selectedLineage[c.Index] = true;
                    }
                }
            }

            Graph = Models.CommitGraph.Generate(commits, commitsChanged, firstParentOnly, highlighting, selectedLineage);
        }

        private void RefreshHoveredLineage()
        {
            if (Preferences.Instance.EnableHoverViewTracking && _hoveredCommitIndex >= 0 && _hoveredCommitIndex < _commits.Count)
            {
                var hoveredIndex = (int)_hoveredCommitIndex;
                var depth = 1000u;
                var topLimit = -1;
                var bottomLimit = -1;

                if (_visibleTopIndex >= 0 && _visibleBottomIndex >= _visibleTopIndex)
                {
                    topLimit = Math.Max(0, _visibleTopIndex - 50);
                    bottomLimit = Math.Min(_commits.Count - 1, _visibleBottomIndex + 50);

                    if (hoveredIndex < topLimit || hoveredIndex > bottomLimit)
                    {
                        topLimit = -1;
                        bottomLimit = -1;
                    }
                }

                HoveredLineageCommits = Models.CommitGraph.GetCommitLineageFast(_commits, _commitMap, _commits[hoveredIndex], LineageSearchMethod, depth, topLimit, bottomLimit);
            }
            else
            {
                HoveredLineageCommits = null;
            }
        }

        private List<Models.Commit> FilterCommits(List<Models.Commit> commits, List<string> targets)
        {
            if (commits == null || commits.Count == 0 || targets.Count == 0)
                return commits;

            var rawCommitMap = new Dictionary<string, Models.Commit>(_rawCommits.Count);
            for (int i = 0; i < _rawCommits.Count; i++)
            {
                _rawCommits[i].Index = i;
                rawCommitMap[_rawCommits[i].SHA] = _rawCommits[i];
            }

            var commitMap = new Dictionary<string, Models.Commit>(commits.Count);
            for (int i = 0; i < commits.Count; i++)
            {
                commits[i].Index = i;
                commitMap[commits[i].SHA] = commits[i];
            }

            var active = new bool[commits.Count];
            foreach (var target in targets)
            {
                string sha = target;
                if (target.Equals("HEAD", StringComparison.OrdinalIgnoreCase))
                {
                    var head = _rawCommits.Find(x => x.IsCurrentHead) ?? commits.Find(x => x.IsCurrentHead);
                    if (head != null)
                        sha = head.SHA;
                }

                if (rawCommitMap.TryGetValue(sha, out var commit))
                {
                    var lineage = Models.CommitGraph.GetCommitLineageFast(_rawCommits, rawCommitMap, commit, LineageSearchMethod, (uint)_rawCommits.Count);
                    for (int i = 0; i < lineage.Length; i++)
                    {
                        if (!lineage[i])
                            continue;

                        var lineageCommit = _rawCommits[i];
                        if (commitMap.TryGetValue(lineageCommit.SHA, out var visibleCommit) && visibleCommit.Index < commits.Count)
                            active[visibleCommit.Index] = true;
                    }
                }
            }

            var result = new List<Models.Commit>();
            for (int i = 0; i < commits.Count; i++)
            {
                if (active[i])
                {
                    var c = commits[i].Clone();
                    c.IsCommitFilterHead = targets.Any(t => t.Equals(c.SHA, StringComparison.OrdinalIgnoreCase) ||
                                                           (t.Equals("HEAD", StringComparison.OrdinalIgnoreCase) && c.IsCurrentHead));
                    result.Add(c);
                }
            }

            return result;
        }

        private List<Models.Commit> FoldCommits(List<Models.Commit> commits)
        {
            if (commits == null || commits.Count == 0)
                return commits;

            if (!Preferences.Instance.EnableLinearCommitFolding)
            {
                foreach (var c in commits)
                    c.IsFolded = false;
                return commits;
            }

            var threshold = Preferences.Instance.MaxLinearCommitsToFold;
            if (threshold < 3)
            {
                foreach (var c in commits)
                    c.IsFolded = false;
                return commits;
            }

            var childrenCount = new Dictionary<string, int>();
            foreach (var c in commits)
            {
                foreach (var p in c.Parents)
                {
                    if (!childrenCount.TryAdd(p, 1))
                        childrenCount[p]++;
                }
            }

            var result = new List<Models.Commit>();
            for (int i = 0; i < commits.Count; i++)
            {
                var start = commits[i];
                if (start.HasDecorators || start.Parents.Count != 1 || childrenCount.GetValueOrDefault(start.SHA, 0) > 1)
                {
                    start.IsFolded = false;
                    result.Add(start);
                    continue;
                }

                var segment = new List<Models.Commit> { start };
                int j = i + 1;
                while (j < commits.Count)
                {
                    var next = commits[j];
                    if (next.HasDecorators || next.Parents.Count != 1 || childrenCount.GetValueOrDefault(next.SHA, 0) > 1 || !segment[^1].Parents[0].Equals(next.SHA))
                        break;

                    segment.Add(next);
                    j++;
                }

                if (segment.Count > threshold)
                {
                    var first = segment[0].Clone();
                    var last = segment[^1];
                    var middleIdx = segment.Count / 2;
                    var middle = segment[middleIdx].Clone();

                    first.IsFolded = false;
                    middle.IsFolded = true;
                    middle.FoldedCount = segment.Count - 3;

                    first.Parents = [middle.SHA];
                    middle.Parents = [last.SHA];

                    result.Add(first);
                    result.Add(middle);
                    result.Add(last);
                }
                else
                {
                    foreach (var c in segment)
                    {
                        c.IsFolded = false;
                        result.Add(c);
                    }
                }

                i = j - 1;
            }

            return result;
        }

        private static bool MatchesState(string filter, Models.Commit commit) => filter switch
        {
            "merged" => commit.IsMerged,
            "unmerged" => !commit.IsMerged,
            "tag" or "tags" => commit.IsTag,
            "branch" or "branches" => commit.HasDecorators && !commit.IsTag,
            "merge" => commit.IsMergeCommit,
            "cherrypick" => commit.IsCherryPicked,
            "head" => commit.IsCurrentHead,
            "folded" => commit.IsFolded,
            _ => true,
        };

        private static bool EvalTerm(string prefix, Models.Commit c, string val) => prefix switch
        {
            "a:" => c.Author.Name.Contains(val, StringComparison.OrdinalIgnoreCase) ||
                        c.Author.Email.Contains(val, StringComparison.OrdinalIgnoreCase),
            "m:" => (c.Subject ?? string.Empty).Contains(val, StringComparison.OrdinalIgnoreCase),
            "t:" => c.Decorators.Any(d => d.Type == Models.DecoratorType.Tag &&
                            d.Name.Contains(val, StringComparison.OrdinalIgnoreCase)),
            "r:" => c.Decorators.Any(d => d.Type == Models.DecoratorType.RemoteBranchHead &&
                            d.Name.Contains(val, StringComparison.OrdinalIgnoreCase)),
            "s:" => c.SHA.Contains(val, StringComparison.OrdinalIgnoreCase),
            "c:" => c.Committer.Name.Contains(val, StringComparison.OrdinalIgnoreCase) ||
                        c.Committer.Email.Contains(val, StringComparison.OrdinalIgnoreCase),
            "e:" => c.Author.Email.Contains(val, StringComparison.OrdinalIgnoreCase) ||
                        c.Committer.Email.Contains(val, StringComparison.OrdinalIgnoreCase),
            "since:" => DateTimeOffset.TryParse(val, out var dtSince) &&
                        DateTimeOffset.FromUnixTimeSeconds((long)c.CommitterTime) >= dtSince,
            "until:" => DateTimeOffset.TryParse(val, out var dtUntil) &&
                        DateTimeOffset.FromUnixTimeSeconds((long)c.CommitterTime) <= dtUntil,
            "is:" => MatchesState(val.ToLowerInvariant(), c),
            _ => true,
        };

        private List<Models.Commit> ApplyGroupFilters(List<Models.Commit> source, Controls.QuerySpec spec)
        {
            var processed = source;

            static IEnumerable<string> PositiveLeaves(Controls.ExprNode node)
            {
                if (node == null)
                    yield break;
                if (node.Op == Controls.ExprOp.Term)
                { yield return node.Value; yield break; }
                if (node.Op == Controls.ExprOp.Not)
                    yield break;
                if (node.Children != null)
                    foreach (var child in node.Children)
                        if (child.Op != Controls.ExprOp.Not)
                            foreach (var v in PositiveLeaves(child))
                                yield return v;
            }

            foreach (var group in spec.Groups)
            {
                if (group.ProviderPrefix is "gitlog:" or "sort:" or "b:" or "t:" or "r:")
                    continue;

                if (group.ProviderPrefix == "solo:")
                {
                    var soloVals = PositiveLeaves(group.Expr).ToList();
                    if (soloVals.Count > 0)
                        processed = FilterCommits(processed, soloVals);
                    continue;
                }

                var prefix = group.ProviderPrefix;
                var expr = group.Expr;
                processed = processed
                    .Where(c => Controls.ExprEvaluator.Evaluate(expr, val => EvalTerm(prefix, c, val)))
                    .ToList();
            }

            return processed;
        }

        private static void ApplyFallbackTerms(Controls.QuerySpec spec, ref List<Models.Commit> processed)
        {
            if (spec.FallbackTerms.Count > 0 || spec.FallbackNotTerms.Count > 0)
            {
                processed = processed.Where(c =>
                {
                    var msg = c.Subject ?? string.Empty;
                    var include = spec.FallbackTerms.Count == 0 ||
                                  spec.FallbackTerms.Any(f => msg.Contains(f, StringComparison.OrdinalIgnoreCase));
                    var exclude = spec.FallbackNotTerms.Any(f => msg.Contains(f, StringComparison.OrdinalIgnoreCase));
                    return include && !exclude;
                }).ToList();
            }
        }

        private Repository _repo = null;
        private List<string> _soloTargets = [];
        private CommitDetailSharedData _commitDetailSharedData = null;
        private bool _isLoading = true;
        private string _searchText = string.Empty;
        private List<Models.Commit> _commits = [];
        private List<Models.Commit> _rawCommits = [];
        private SuggestionCacheManager _cacheManager;
        private Dictionary<string, List<Models.Commit>> _suggestionCache = new();
        private Models.CommitGraph _graph = null;
        private long _hoveredCommitIndex = -1;
        private bool[] _hoveredLineageCommits = null;
        private List<Models.Commit> _selectedCommits = [];
        private Models.Bisect _bisect = null;
        private object _detailContext = new Models.Null();
        private bool _ignoreSelectionChange = false;

        private GridLength _leftArea = new(1, GridUnitType.Star);
        private GridLength _rightArea = new(1, GridUnitType.Star);
        private GridLength _topArea = new(1, GridUnitType.Star);
        private GridLength _bottomArea = new(1, GridUnitType.Star);
        private bool _isMaximizeDetails = false;
        private bool _isCollapseDetails = false;
        private HashSet<int> _selectedLineagePaths = null;
        private bool[] _selectedLineageCommits = null;
        private int _visibleTopIndex = -1;
        private int _visibleBottomIndex = -1;
        private Dictionary<string, Models.Commit> _commitMap = new();
        private bool _gitOptionsDrivenByTokens = false;
        private bool _suppressSearchTokenCollectionChanged = false;
        private bool _syncingRepoFlagsFromTokens = false;
        private readonly List<Models.HistoryFilter> _searchDrivenFilters = [];
        private bool _suppressGraphRefreshFromSelectionChange = false;
        private int _lineageRequestVersion = 0;
    }
}
