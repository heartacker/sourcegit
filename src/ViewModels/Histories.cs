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
                    "solo:",
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

                var stateFilters = SearchTokens
                    .Where(t => t.StartsWith("is:", StringComparison.OrdinalIgnoreCase))
                    .Select(t => t.Substring(3).Trim().ToLowerInvariant())
                    .Where(t => !string.IsNullOrEmpty(t))
                    .ToList();

                var excludedStateFilters = SearchTokens
                    .Where(t => t.StartsWith("!is:", StringComparison.OrdinalIgnoreCase))
                    .Select(t => t.Substring(4).Trim().ToLowerInvariant())
                    .Where(t => !string.IsNullOrEmpty(t))
                    .ToList();

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

                var soloFilters = SearchTokens
                    .Where(t => t.StartsWith("solo:", StringComparison.OrdinalIgnoreCase))
                    .Select(t => t.Substring(t.IndexOf(':') + 1).Trim())
                    .Where(t => !string.IsNullOrEmpty(t))
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
                        var lineage = Models.CommitGraph.GetCommitLineageFast(
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

                if (stateFilters.Count > 0 || excludedStateFilters.Count > 0)
                {
                    processed = processed.Where(c =>
                    {
                        bool Matches(string filter, Models.Commit commit)
                        {
                            return filter switch
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
                        }

                        var include = stateFilters.All(f => Matches(f, c));
                        var exclude = excludedStateFilters.Any(f => Matches(f, c));
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

                if (soloFilters.Count > 0)
                {
                    processed = FilterCommits(processed, soloFilters);
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

            _repo.UIStates.HistoryFilters.CollectionChanged += (_, _) => UpdateDisplayCommits();
            Preferences.Instance.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(Preferences.EnableLinearCommitFolding))
                    UpdateDisplayCommits();
            };

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
                groupAdvanced, isProv, Controls.TokenLogicMode.AutoAnd, icon: implementedIcon));
            SearchProviders.Add(new Controls.StaticTokenSuggestionProvider("a:", "作者", groupFilters, suggester: authorSuggester, logicMode: Controls.TokenLogicMode.AutoOr, alias: new[] { "author:" }, icon: implementedIcon));
            SearchProviders.Add(new Controls.StaticTokenSuggestionProvider("m:", "提交消息", groupFilters, suggester: messageSuggester, logicMode: Controls.TokenLogicMode.AutoOr, alias: new[] { "message:" }, icon: implementedIcon));
            SearchProviders.Add(new Controls.StaticTokenSuggestionProvider("b:", "分支", groupFilters, suggester: branchSuggester, logicMode: Controls.TokenLogicMode.AutoOr, alias: new[] { "branch:" }, icon: implementedIcon));
            SearchProviders.Add(new Controls.StaticTokenSuggestionProvider("solo:", "Solo 提交链过滤", groupView, new[] { "HEAD" }, Controls.TokenLogicMode.AutoOr, icon: implementedIcon));
            SearchProviders.Add(new Controls.StaticTokenSuggestionProvider("t:", "标签", groupFilters, alias: new[] { "tag:" }, icon: implementedIcon));
            SearchProviders.Add(new Controls.StaticTokenSuggestionProvider("r:", "远程分支", groupFilters, alias: new[] { "remote:" }, icon: implementedIcon));
            SearchProviders.Add(new Controls.StaticTokenSuggestionProvider("f:", "文件路径", groupFilters, alias: new[] { "file:" }));
            SearchProviders.Add(new Controls.StaticTokenSuggestionProvider("p:", "路径", groupFilters, alias: new[] { "path:" }));
            SearchProviders.Add(new Controls.StaticTokenSuggestionProvider("s:", "哈希", groupFilters, alias: new[] { "sha:" }, icon: implementedIcon));
            SearchProviders.Add(new Controls.StaticTokenSuggestionProvider("since:", "起始时间", groupFilters, alias: new[] { "after:" }, icon: implementedIcon));
            SearchProviders.Add(new Controls.StaticTokenSuggestionProvider("until:", "结束时间", groupFilters, alias: new[] { "before:" }, icon: implementedIcon));

            SearchProviders.Add(new Controls.StaticTokenSuggestionProvider("c:", "提交者", groupAdvanced, alias: new[] { "committer:" }, icon: implementedIcon));
            SearchProviders.Add(new Controls.StaticTokenSuggestionProvider("e:", "邮箱", groupAdvanced, alias: new[] { "email:" }, icon: implementedIcon));
            SearchProviders.Add(new Controls.StaticTokenSuggestionProvider("S:", "内容搜索 (Pickaxe)", groupAdvanced));
            SearchProviders.Add(new Controls.StaticTokenSuggestionProvider("G:", "正则搜索 (Grep)", groupAdvanced));
            SearchProviders.Add(new Controls.StaticTokenSuggestionProvider("change:", "变更类型", groupAdvanced));
            SearchProviders.Add(new Controls.StaticTokenSuggestionProvider("signed:", "GPG 签名状态", groupAdvanced));
            SearchProviders.Add(new Controls.StaticTokenSuggestionProvider("parent:", "父提交搜索", groupAdvanced));

            SearchProviders.Add(new Controls.StaticTokenSuggestionProvider("ui:", "UI 控制指令", groupView, logicMode: Controls.TokenLogicMode.SingleReplace, icon: implementedIcon));
            SearchProviders.Add(new Controls.StaticTokenSuggestionProvider("sort:", "排序方式", groupView, new[] { "Commit Date", "Topologically" }, Controls.TokenLogicMode.SingleReplace));

            SearchProviders.Add(new Controls.StaticTokenSuggestionProvider("git:", "git 解析选项", groupGit, new[] { "--reflog", "--first-parent", "--simplify-by-decoration" }, Controls.TokenLogicMode.AutoOr));

            SearchTokens.CollectionChanged += (_, e) =>
            {
                if (_suppressNextTokenCollectionRefresh)
                {
                    _suppressNextTokenCollectionRefresh = false;
                    return;
                }

                var gitOptions = SearchTokens
                    .Where(t => t.StartsWith("git:", StringComparison.OrdinalIgnoreCase))
                    .Select(t => t.Substring(t.IndexOf(':') + 1).Trim())
                    .Where(t => !string.IsNullOrEmpty(t))
                    .ToList();

                if (gitOptions.Count > 0)
                {
                    var flags = Models.HistoryShowFlags.None;
                    if (gitOptions.Any(o => o.Equals("--reflog", StringComparison.OrdinalIgnoreCase)))
                        flags |= Models.HistoryShowFlags.Reflog;
                    if (gitOptions.Any(o => o.Equals("--first-parent", StringComparison.OrdinalIgnoreCase)))
                        flags |= Models.HistoryShowFlags.FirstParentOnly;
                    if (gitOptions.Any(o => o.Equals("--simplify-by-decoration", StringComparison.OrdinalIgnoreCase)))
                        flags |= Models.HistoryShowFlags.SimplifyByDecoration;

                    if (_repo.HistoryShowFlags != flags)
                        _repo.HistoryShowFlags = flags;

                    _gitOptionsDrivenByTokens = true;
                }
                else if (_gitOptionsDrivenByTokens)
                {
                    if (_repo.HistoryShowFlags != Models.HistoryShowFlags.None)
                        _repo.HistoryShowFlags = Models.HistoryShowFlags.None;

                    _gitOptionsDrivenByTokens = false;
                }

                if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Add)
                {
                    foreach (string token in e.NewItems)
                    {
                        if (token.Equals("ui:author", StringComparison.OrdinalIgnoreCase))
                        {
                            IsAuthorColumnVisible = !IsAuthorColumnVisible;
                            Dispatcher.UIThread.Post(() =>
                            {
                                _suppressNextTokenCollectionRefresh = true;
                                SearchTokens.Remove(token);
                            });
                        }
                        else if (token.Equals("ui:sha", StringComparison.OrdinalIgnoreCase))
                        {
                            IsSHAColumnVisible = !IsSHAColumnVisible;
                            Dispatcher.UIThread.Post(() =>
                            {
                                _suppressNextTokenCollectionRefresh = true;
                                SearchTokens.Remove(token);
                            });
                        }
                        else if (token.Equals("ui:time", StringComparison.OrdinalIgnoreCase))
                        {
                            IsDateTimeColumnVisible = !IsDateTimeColumnVisible;
                            Dispatcher.UIThread.Post(() =>
                            {
                                _suppressNextTokenCollectionRefresh = true;
                                SearchTokens.Remove(token);
                            });
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

        private Repository _repo = null;
        private List<string> _soloTargets = [];
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
        private bool _gitOptionsDrivenByTokens = false;
        private bool _suppressNextTokenCollectionRefresh = false;
        private bool _suppressGraphRefreshFromSelectionChange = false;
        private int _lineageRequestVersion = 0;
    }
}
