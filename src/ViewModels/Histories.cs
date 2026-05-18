using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
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

        public AvaloniaList<Models.IHistoryViewFilter> ViewFilters { get; } = [];

        public List<string> SoloTargets
        {
            get
            {
                var solo = ViewFilters.OfType<Models.SoloFilter>().FirstOrDefault();
                return solo?.Targets ?? [];
            }
        }

        public bool HasActiveViewFilters
        {
            get => ViewFilters.Any(x => x.IsActive);
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

            // Apply Token Filters
            if (SearchTokens.Count > 0)
            {
                var authorFilters = SearchTokens
                    .Where(t => t.StartsWith("author:", StringComparison.OrdinalIgnoreCase) || t.StartsWith("a:", StringComparison.OrdinalIgnoreCase))
                    .Select(t => t.Substring(t.IndexOf(':') + 1).Trim())
                    .Where(t => !string.IsNullOrEmpty(t))
                    .ToList();

                var excludedAuthorFilters = SearchTokens
                    .Where(t => t.StartsWith("!author:", StringComparison.OrdinalIgnoreCase) || t.StartsWith("!a:", StringComparison.OrdinalIgnoreCase))
                    .Select(t => t.Substring(t.IndexOf(':') + 1).Trim())
                    .Where(t => !string.IsNullOrEmpty(t))
                    .ToList();

                var branchFilters = SearchTokens
                    .Where(t => t.StartsWith("branch:", StringComparison.OrdinalIgnoreCase) || t.StartsWith("b:", StringComparison.OrdinalIgnoreCase))
                    .Select(t => t.Substring(t.IndexOf(':') + 1).Trim())
                    .Where(t => !string.IsNullOrEmpty(t))
                    .ToList();

                var excludedBranchFilters = SearchTokens
                    .Where(t => t.StartsWith("!branch:", StringComparison.OrdinalIgnoreCase) || t.StartsWith("!b:", StringComparison.OrdinalIgnoreCase))
                    .Select(t => t.Substring(t.IndexOf(':') + 1).Trim())
                    .Where(t => !string.IsNullOrEmpty(t))
                    .ToList();

                var messageFilters = SearchTokens
                    .Where(t => t.StartsWith("message:", StringComparison.OrdinalIgnoreCase) || t.StartsWith("m:", StringComparison.OrdinalIgnoreCase))
                    .Select(t => t.Substring(t.IndexOf(':') + 1).Trim())
                    .Where(t => !string.IsNullOrEmpty(t))
                    .ToList();

                var excludedMessageFilters = SearchTokens
                    .Where(t => t.StartsWith("!message:", StringComparison.OrdinalIgnoreCase) || t.StartsWith("!m:", StringComparison.OrdinalIgnoreCase))
                    .Select(t => t.Substring(t.IndexOf(':') + 1).Trim())
                    .Where(t => !string.IsNullOrEmpty(t))
                    .ToList();

                var knownPrefixes = new[]
                {
                    "is:",
                    "author:", "a:",
                    "message:", "m:",
                    "branch:", "b:",
                    "tag:", "t:",
                    "remote:", "r:",
                    "file:", "f:",
                    "path:", "p:",
                    "sha:", "s:",
                    "since:", "after:", "until:", "before:",
                    "committer:", "c:",
                    "email:", "e:",
                    "S:", "G:",
                    "change:", "signed:", "parent:",
                    "ui:", "sort:", "git:",
                };

                static string ExtractUnknownMessageTerm(string token, string[] prefixes)
                {
                    if (string.IsNullOrWhiteSpace(token) || token == "|" || token == "&")
                        return null;

                    var check = token.StartsWith("!", StringComparison.Ordinal) ? token[1..] : token;
                    if (prefixes.Any(p => check.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
                        return null;

                    var idx = check.IndexOf(':');
                    var term = idx >= 0 ? check[(idx + 1)..].Trim() : check.Trim();
                    return string.IsNullOrEmpty(term) ? null : term;
                }

                var fallbackMessageFilters = SearchTokens
                    .Where(t => !t.StartsWith("!", StringComparison.Ordinal))
                    .Select(t => ExtractUnknownMessageTerm(t, knownPrefixes))
                    .Where(t => !string.IsNullOrEmpty(t))
                    .ToList();

                var fallbackExcludedMessageFilters = SearchTokens
                    .Where(t => t.StartsWith("!", StringComparison.Ordinal))
                    .Select(t => ExtractUnknownMessageTerm(t, knownPrefixes))
                    .Where(t => !string.IsNullOrEmpty(t))
                    .ToList();

                messageFilters.AddRange(fallbackMessageFilters);
                excludedMessageFilters.AddRange(fallbackExcludedMessageFilters);

                var tagFilters = SearchTokens
                    .Where(t => t.StartsWith("tag:", StringComparison.OrdinalIgnoreCase) || t.StartsWith("t:", StringComparison.OrdinalIgnoreCase))
                    .Select(t => t.Substring(t.IndexOf(':') + 1).Trim())
                    .Where(t => !string.IsNullOrEmpty(t))
                    .ToList();

                var excludedTagFilters = SearchTokens
                    .Where(t => t.StartsWith("!tag:", StringComparison.OrdinalIgnoreCase) || t.StartsWith("!t:", StringComparison.OrdinalIgnoreCase))
                    .Select(t => t.Substring(t.IndexOf(':') + 1).Trim())
                    .Where(t => !string.IsNullOrEmpty(t))
                    .ToList();

                var remoteFilters = SearchTokens
                    .Where(t => t.StartsWith("remote:", StringComparison.OrdinalIgnoreCase) || t.StartsWith("r:", StringComparison.OrdinalIgnoreCase))
                    .Select(t => t.Substring(t.IndexOf(':') + 1).Trim())
                    .Where(t => !string.IsNullOrEmpty(t))
                    .ToList();

                var excludedRemoteFilters = SearchTokens
                    .Where(t => t.StartsWith("!remote:", StringComparison.OrdinalIgnoreCase) || t.StartsWith("!r:", StringComparison.OrdinalIgnoreCase))
                    .Select(t => t.Substring(t.IndexOf(':') + 1).Trim())
                    .Where(t => !string.IsNullOrEmpty(t))
                    .ToList();

                var shaFilters = SearchTokens
                    .Where(t => t.StartsWith("sha:", StringComparison.OrdinalIgnoreCase) || t.StartsWith("s:", StringComparison.OrdinalIgnoreCase))
                    .Select(t => t.Substring(t.IndexOf(':') + 1).Trim())
                    .Where(t => !string.IsNullOrEmpty(t))
                    .ToList();

                var excludedShaFilters = SearchTokens
                    .Where(t => t.StartsWith("!sha:", StringComparison.OrdinalIgnoreCase) || t.StartsWith("!s:", StringComparison.OrdinalIgnoreCase))
                    .Select(t => t.Substring(t.IndexOf(':') + 1).Trim())
                    .Where(t => !string.IsNullOrEmpty(t))
                    .ToList();

                var committerFilters = SearchTokens
                    .Where(t => t.StartsWith("committer:", StringComparison.OrdinalIgnoreCase) || t.StartsWith("c:", StringComparison.OrdinalIgnoreCase))
                    .Select(t => t.Substring(t.IndexOf(':') + 1).Trim())
                    .Where(t => !string.IsNullOrEmpty(t))
                    .ToList();

                var excludedCommitterFilters = SearchTokens
                    .Where(t => t.StartsWith("!committer:", StringComparison.OrdinalIgnoreCase) || t.StartsWith("!c:", StringComparison.OrdinalIgnoreCase))
                    .Select(t => t.Substring(t.IndexOf(':') + 1).Trim())
                    .Where(t => !string.IsNullOrEmpty(t))
                    .ToList();

                var emailFilters = SearchTokens
                    .Where(t => t.StartsWith("email:", StringComparison.OrdinalIgnoreCase) || t.StartsWith("e:", StringComparison.OrdinalIgnoreCase))
                    .Select(t => t.Substring(t.IndexOf(':') + 1).Trim())
                    .Where(t => !string.IsNullOrEmpty(t))
                    .ToList();

                var excludedEmailFilters = SearchTokens
                    .Where(t => t.StartsWith("!email:", StringComparison.OrdinalIgnoreCase) || t.StartsWith("!e:", StringComparison.OrdinalIgnoreCase))
                    .Select(t => t.Substring(t.IndexOf(':') + 1).Trim())
                    .Where(t => !string.IsNullOrEmpty(t))
                    .ToList();

                var sinceFilters = SearchTokens
                    .Where(t => t.StartsWith("since:", StringComparison.OrdinalIgnoreCase) || t.StartsWith("after:", StringComparison.OrdinalIgnoreCase))
                    .Select(t => t.Substring(t.IndexOf(':') + 1).Trim())
                    .Where(t => !string.IsNullOrEmpty(t))
                    .Select(v => DateTimeOffset.TryParse(v, out var dt) ? (DateTimeOffset?)dt : null)
                    .Where(dt => dt.HasValue)
                    .Select(dt => dt.Value)
                    .ToList();

                var excludedSinceFilters = SearchTokens
                    .Where(t => t.StartsWith("!since:", StringComparison.OrdinalIgnoreCase) || t.StartsWith("!after:", StringComparison.OrdinalIgnoreCase))
                    .Select(t => t.Substring(t.IndexOf(':') + 1).Trim())
                    .Where(t => !string.IsNullOrEmpty(t))
                    .Select(v => DateTimeOffset.TryParse(v, out var dt) ? (DateTimeOffset?)dt : null)
                    .Where(dt => dt.HasValue)
                    .Select(dt => dt.Value)
                    .ToList();

                var untilFilters = SearchTokens
                    .Where(t => t.StartsWith("until:", StringComparison.OrdinalIgnoreCase) || t.StartsWith("before:", StringComparison.OrdinalIgnoreCase))
                    .Select(t => t.Substring(t.IndexOf(':') + 1).Trim())
                    .Where(t => !string.IsNullOrEmpty(t))
                    .Select(v => DateTimeOffset.TryParse(v, out var dt) ? (DateTimeOffset?)dt : null)
                    .Where(dt => dt.HasValue)
                    .Select(dt => dt.Value)
                    .ToList();

                var excludedUntilFilters = SearchTokens
                    .Where(t => t.StartsWith("!until:", StringComparison.OrdinalIgnoreCase) || t.StartsWith("!before:", StringComparison.OrdinalIgnoreCase))
                    .Select(t => t.Substring(t.IndexOf(':') + 1).Trim())
                    .Where(t => !string.IsNullOrEmpty(t))
                    .Select(v => DateTimeOffset.TryParse(v, out var dt) ? (DateTimeOffset?)dt : null)
                    .Where(dt => dt.HasValue)
                    .Select(dt => dt.Value)
                    .ToList();

                bool MatchesBranchHead(Models.Commit commit, string filter)
                {
                    return commit.Decorators.Any(d =>
                        (d.Type is Models.DecoratorType.LocalBranchHead or Models.DecoratorType.RemoteBranchHead or Models.DecoratorType.CurrentBranchHead) &&
                        d.Name.Contains(filter, StringComparison.OrdinalIgnoreCase));
                }

                HashSet<string> CollectBranchLineageShas(IEnumerable<string> filters)
                {
                    var commits = _rawCommits;
                    var map = new Dictionary<string, Models.Commit>(commits.Count);
                    for (int i = 0; i < commits.Count; i++)
                    {
                        commits[i].Index = i;
                        map[commits[i].SHA] = commits[i];
                    }

                    var seeds = commits
                        .Where(c => filters.Any(f => MatchesBranchHead(c, f)))
                        .ToList();

                    var result = new HashSet<string>();
                    foreach (var seed in seeds)
                    {
                        var lineage = Models.CommitGraph.GetLineage(
                            commits,
                            map,
                            seed,
                            Models.CommitLineageSearchMethod.FullLineage,
                            (uint)commits.Count);

                        for (int i = 0; i < lineage.Length; i++)
                        {
                            if (lineage[i])
                                result.Add(commits[i].SHA);
                        }
                    }

                    return result;
                }

                if (branchFilters.Count > 0 || excludedBranchFilters.Count > 0)
                {
                    HashSet<string> includeSet = null;
                    HashSet<string> excludeSet = null;

                    if (branchFilters.Count > 0)
                        includeSet = CollectBranchLineageShas(branchFilters);
                    if (excludedBranchFilters.Count > 0)
                        excludeSet = CollectBranchLineageShas(excludedBranchFilters);

                    processed = processed.Where(c =>
                    {
                        var include = includeSet == null || includeSet.Contains(c.SHA);
                        var exclude = excludeSet != null && excludeSet.Contains(c.SHA);
                        return include && !exclude;
                    }).ToList();
                }

                if (tagFilters.Count > 0 || excludedTagFilters.Count > 0)
                {
                    processed = processed.Where(c =>
                    {
                        var tagNames = c.Decorators
                            .Where(d => d.Type == Models.DecoratorType.Tag)
                            .Select(d => d.Name)
                            .ToList();

                        var include = tagFilters.Count == 0 || tagFilters.Any(f => tagNames.Any(name => name.Contains(f, StringComparison.OrdinalIgnoreCase)));
                        var exclude = excludedTagFilters.Any(f => tagNames.Any(name => name.Contains(f, StringComparison.OrdinalIgnoreCase)));
                        return include && !exclude;
                    }).ToList();
                }

                if (remoteFilters.Count > 0 || excludedRemoteFilters.Count > 0)
                {
                    processed = processed.Where(c =>
                    {
                        var remoteNames = c.Decorators
                            .Where(d => d.Type == Models.DecoratorType.RemoteBranchHead)
                            .Select(d => d.Name)
                            .ToList();

                        var include = remoteFilters.Count == 0 || remoteFilters.Any(f => remoteNames.Any(name => name.Contains(f, StringComparison.OrdinalIgnoreCase)));
                        var exclude = excludedRemoteFilters.Any(f => remoteNames.Any(name => name.Contains(f, StringComparison.OrdinalIgnoreCase)));
                        return include && !exclude;
                    }).ToList();
                }

                if (authorFilters.Count > 0 || excludedAuthorFilters.Count > 0)
                {
                    processed = processed.Where(c =>
                    {
                        var include = authorFilters.Count == 0 || authorFilters.Any(f => c.Author.Name.Contains(f, StringComparison.OrdinalIgnoreCase) || c.Author.Email.Contains(f, StringComparison.OrdinalIgnoreCase));
                        var exclude = excludedAuthorFilters.Any(f => c.Author.Name.Contains(f, StringComparison.OrdinalIgnoreCase) || c.Author.Email.Contains(f, StringComparison.OrdinalIgnoreCase));
                        return include && !exclude;
                    }).ToList();
                }

                if (committerFilters.Count > 0 || excludedCommitterFilters.Count > 0)
                {
                    processed = processed.Where(c =>
                    {
                        var include = committerFilters.Count == 0 || committerFilters.Any(f => c.Committer.Name.Contains(f, StringComparison.OrdinalIgnoreCase) || c.Committer.Email.Contains(f, StringComparison.OrdinalIgnoreCase));
                        var exclude = excludedCommitterFilters.Any(f => c.Committer.Name.Contains(f, StringComparison.OrdinalIgnoreCase) || c.Committer.Email.Contains(f, StringComparison.OrdinalIgnoreCase));
                        return include && !exclude;
                    }).ToList();
                }

                if (emailFilters.Count > 0 || excludedEmailFilters.Count > 0)
                {
                    processed = processed.Where(c =>
                    {
                        var include = emailFilters.Count == 0 || emailFilters.Any(f =>
                            c.Author.Email.Contains(f, StringComparison.OrdinalIgnoreCase) ||
                            c.Committer.Email.Contains(f, StringComparison.OrdinalIgnoreCase));
                        var exclude = excludedEmailFilters.Any(f =>
                            c.Author.Email.Contains(f, StringComparison.OrdinalIgnoreCase) ||
                            c.Committer.Email.Contains(f, StringComparison.OrdinalIgnoreCase));
                        return include && !exclude;
                    }).ToList();
                }

                if (shaFilters.Count > 0 || excludedShaFilters.Count > 0)
                {
                    processed = processed.Where(c =>
                    {
                        var include = shaFilters.Count == 0 || shaFilters.Any(f => c.SHA.Contains(f, StringComparison.OrdinalIgnoreCase));
                        var exclude = excludedShaFilters.Any(f => c.SHA.Contains(f, StringComparison.OrdinalIgnoreCase));
                        return include && !exclude;
                    }).ToList();
                }

                if (sinceFilters.Count > 0 || excludedSinceFilters.Count > 0 || untilFilters.Count > 0 || excludedUntilFilters.Count > 0)
                {
                    processed = processed.Where(c =>
                    {
                        var commitTime = DateTimeOffset.FromUnixTimeSeconds((long)c.CommitterTime);

                        var includeSince = sinceFilters.Count == 0 || sinceFilters.Any(t => commitTime >= t);
                        var excludeSince = excludedSinceFilters.Any(t => commitTime >= t);
                        var includeUntil = untilFilters.Count == 0 || untilFilters.Any(t => commitTime <= t);
                        var excludeUntil = excludedUntilFilters.Any(t => commitTime <= t);

                        return includeSince && !excludeSince && includeUntil && !excludeUntil;
                    }).ToList();
                }

                if (messageFilters.Count > 0 || excludedMessageFilters.Count > 0)
                {
                    processed = processed.Where(c =>
                    {
                        var message = c.Subject ?? string.Empty;
                        var include = messageFilters.Count == 0 || messageFilters.Any(f => message.Contains(f, StringComparison.OrdinalIgnoreCase));
                        var exclude = excludedMessageFilters.Any(f => message.Contains(f, StringComparison.OrdinalIgnoreCase));
                        return include && !exclude;
                    }).ToList();
                }
            }

            foreach (var filter in ViewFilters)
                processed = filter.Process(processed, _commitMap);

            GenerateGraph(_rawCommits, false);

            if (SetProperty(ref _commits, processed, nameof(Commits)))
            {
                PostCommitsChanged();
            }

            OnPropertyChanged(nameof(HasActiveViewFilters));
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
                {
                    if (Preferences.Instance.EnableHoverViewTracking && value >= 0 && value < _commits.Count)
                    {
                        var hoveredIndex = (int)value;
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

                        HoveredLineageCommits = Models.CommitGraph.GetLineage(_commits, _commitMap, _commits[hoveredIndex], LineageSearchMethod, depth, topLimit, bottomLimit);
                    }
                    else
                    {
                        HoveredLineageCommits = null;
                    }
                }
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

                    if (_repo.UIStates.GraphHighlighting >= Models.CommitGraphHighlighting.SelectedCommitsOnly)
                    {
                        if (_selectedCommits.Count == 1)
                            CalculateTargetLineage(_selectedCommits[0]);
                        else
                            GenerateGraph(_commits);
                    }
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

            var solo = new Models.SoloFilter();
            var folding = new Models.FoldingFilter();
            ViewFilters.Add(solo);
            ViewFilters.Add(folding);

            ViewFilters.CollectionChanged += (_, _) => UpdateDisplayCommits();
            _repo.UIStates.HistoryFilters.CollectionChanged += (_, _) => UpdateDisplayCommits();
            Preferences.Instance.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(Preferences.EnableLinearCommitFolding))
                {
                    ViewFilters.OfType<Models.FoldingFilter>().FirstOrDefault()?.NotifyStateChanged();
                    UpdateDisplayCommits();
                }
            };

            foreach (var filter in ViewFilters)
            {
                if (filter is Models.SoloFilter soloFilter)
                    soloFilter.PropertyChanged += (_, e) => OnPropertyChanged(nameof(HasActiveViewFilters));
                if (filter is Models.FoldingFilter foldingFilter)
                    foldingFilter.PropertyChanged += (_, e) => OnPropertyChanged(nameof(HasActiveViewFilters));
            }

            Func<string, System.Threading.CancellationToken, Task<IEnumerable<Controls.TokenSuggestion>>> authorSuggester = (pattern, ct) =>
            {
                var authors = _rawCommits.Select(c => c.Author).DistinctBy(a => a.Name);
                var suggestions = authors
                    .Where(a => string.IsNullOrEmpty(pattern) || a.Name.Contains(pattern, StringComparison.OrdinalIgnoreCase) || a.Email.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                    .Select(a => new Controls.TokenSuggestion { Name = a.Name, Description = a.Email });
                return Task.FromResult(suggestions);
            };

            Func<string, System.Threading.CancellationToken, Task<IEnumerable<Controls.TokenSuggestion>>> branchSuggester = (pattern, ct) =>
            {
                var branchNames = _rawCommits
                    .SelectMany(c => c.Decorators)
                    .Where(d => d.Type is Models.DecoratorType.LocalBranchHead or Models.DecoratorType.RemoteBranchHead or Models.DecoratorType.CurrentBranchHead)
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

            Func<string, System.Threading.CancellationToken, Task<IEnumerable<Controls.TokenSuggestion>>> messageSuggester = (pattern, ct) =>
            {
                // var subjects = _rawCommits
                //     .Select(c => c.Subject)
                //     .Where(s => !string.IsNullOrEmpty(s))
                //     .Distinct(StringComparer.OrdinalIgnoreCase);

                // var suggestions = subjects
                //     .Where(s => string.IsNullOrEmpty(pattern) || s.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                //     .Take(100)
                //     .Select(s => new Controls.TokenSuggestion { Name = s });
                return Task.FromResult(Enumerable.Empty<Controls.TokenSuggestion>());
            };

            var groupFilters = new Controls.TokenSuggestionGroup("filters", "常规过滤");
            var groupAdvanced = new Controls.TokenSuggestionGroup("advanced", "高级检索");
            var groupView = new Controls.TokenSuggestionGroup("view", "视图控制");
            var groupGit = new Controls.TokenSuggestionGroup("git", "Git 选项");
            var implementedIcon = "M 1.5 6.5 L 4.5 9.5 L 10.5 2.5";

            SearchProviders.Add(new Controls.StaticTokenSuggestionProvider("is:", "状态过滤 (如 is:unread, is:merged)", groupAdvanced));
            SearchProviders.Add(new Controls.StaticTokenSuggestionProvider("author:", "作者全称", groupFilters, authorSuggester, Controls.TokenLogicMode.AutoOr, alias: new[] { "a:" }, icon: implementedIcon));
            SearchProviders.Add(new Controls.StaticTokenSuggestionProvider("message:", "提交消息全称", groupFilters, messageSuggester, Controls.TokenLogicMode.AutoOr, alias: new[] { "m:" }, icon: implementedIcon));
            SearchProviders.Add(new Controls.StaticTokenSuggestionProvider("branch:", "分支全称", groupFilters, branchSuggester, Controls.TokenLogicMode.AutoOr, alias: new[] { "b:" }, icon: implementedIcon));
            SearchProviders.Add(new Controls.StaticTokenSuggestionProvider("tag:", "标签全称", groupFilters, icon: implementedIcon));
            SearchProviders.Add(new Controls.StaticTokenSuggestionProvider("t:", "标签简写", groupFilters, icon: implementedIcon));
            SearchProviders.Add(new Controls.StaticTokenSuggestionProvider("remote:", "远程分支全称", groupFilters, icon: implementedIcon));
            SearchProviders.Add(new Controls.StaticTokenSuggestionProvider("r:", "远程分支简写", groupFilters, icon: implementedIcon));
            SearchProviders.Add(new Controls.StaticTokenSuggestionProvider("file:", "文件路径全称", groupFilters));
            SearchProviders.Add(new Controls.StaticTokenSuggestionProvider("f:", "文件路径简写", groupFilters));
            SearchProviders.Add(new Controls.StaticTokenSuggestionProvider("path:", "路径别名", groupFilters));
            SearchProviders.Add(new Controls.StaticTokenSuggestionProvider("p:", "路径简写", groupFilters));
            SearchProviders.Add(new Controls.StaticTokenSuggestionProvider("sha:", "哈希全称", groupFilters, icon: implementedIcon));
            SearchProviders.Add(new Controls.StaticTokenSuggestionProvider("s:", "哈希简写", groupFilters, icon: implementedIcon));
            SearchProviders.Add(new Controls.StaticTokenSuggestionProvider("since:", "起始时间", groupFilters, icon: implementedIcon));
            SearchProviders.Add(new Controls.StaticTokenSuggestionProvider("after:", "起始时间别名", groupFilters, icon: implementedIcon));
            SearchProviders.Add(new Controls.StaticTokenSuggestionProvider("until:", "结束时间", groupFilters, icon: implementedIcon));
            SearchProviders.Add(new Controls.StaticTokenSuggestionProvider("before:", "结束时间别名", groupFilters, icon: implementedIcon));

            SearchProviders.Add(new Controls.StaticTokenSuggestionProvider("committer:", "提交者全称", groupAdvanced, icon: implementedIcon));
            SearchProviders.Add(new Controls.StaticTokenSuggestionProvider("c:", "提交者简写", groupAdvanced, icon: implementedIcon));
            SearchProviders.Add(new Controls.StaticTokenSuggestionProvider("email:", "邮箱全称", groupAdvanced, icon: implementedIcon));
            SearchProviders.Add(new Controls.StaticTokenSuggestionProvider("e:", "邮箱简写", groupAdvanced, icon: implementedIcon));
            SearchProviders.Add(new Controls.StaticTokenSuggestionProvider("S:", "内容搜索 (Pickaxe)", groupAdvanced));
            SearchProviders.Add(new Controls.StaticTokenSuggestionProvider("G:", "正则搜索 (Grep)", groupAdvanced));
            SearchProviders.Add(new Controls.StaticTokenSuggestionProvider("change:", "变更类型 (added, deleted)", groupAdvanced));
            SearchProviders.Add(new Controls.StaticTokenSuggestionProvider("signed:", "GPG 签名状态", groupAdvanced));
            SearchProviders.Add(new Controls.StaticTokenSuggestionProvider("parent:", "父提交搜索", groupAdvanced));

            SearchProviders.Add(new Controls.StaticTokenSuggestionProvider("ui:", "UI 控制指令 (如 ui:author)", groupView, logicMode: Controls.TokenLogicMode.SingleReplace, icon: implementedIcon));
            SearchProviders.Add(new Controls.StaticTokenSuggestionProvider("sort:", "排序方式", groupView, new[] {
                            "Commit Date",
                            "Topologically" },
                            Controls.TokenLogicMode.SingleReplace));

            SearchProviders.Add(new Controls.StaticTokenSuggestionProvider("git:", "git 解析选项", groupGit,
                            new[]
                            {
                                "--reflog",
                                "--first-parent",
                                "--simplify-by-decoration",
                            },
                            logicMode: Controls.TokenLogicMode.AutoOr));

            SearchTokens.CollectionChanged += (_, e) =>
            {
                if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Add)
                {
                    foreach (string token in e.NewItems)
                    {
                        if (token.Equals("ui:author", StringComparison.OrdinalIgnoreCase))
                        {
                            IsAuthorColumnVisible = !IsAuthorColumnVisible;
                            Dispatcher.UIThread.Post(() => SearchTokens.Remove(token));
                        }
                        else if (token.Equals("ui:sha", StringComparison.OrdinalIgnoreCase))
                        {
                            IsSHAColumnVisible = !IsSHAColumnVisible;
                            Dispatcher.UIThread.Post(() => SearchTokens.Remove(token));
                        }
                        else if (token.Equals("ui:time", StringComparison.OrdinalIgnoreCase))
                        {
                            IsDateTimeColumnVisible = !IsDateTimeColumnVisible;
                            Dispatcher.UIThread.Post(() => SearchTokens.Remove(token));
                        }
                    }
                }

                UpdateDisplayCommits();
            };
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
            Task.Run(() =>
            {
                if (commit == null)
                {
                    Dispatcher.UIThread.Post(() =>
                    {
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
                    CalculateTargetLineage(c);
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

        private Repository _repo = null;
        private CommitDetailSharedData _commitDetailSharedData = null;
        private bool _isLoading = true;
        private string _searchText = string.Empty;
        private List<Models.Commit> _commits = [];
        private List<Models.Commit> _rawCommits = [];
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
    }
}
