using System;
using System.Collections;
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
using SourceGit.Controls;

namespace SourceGit.ViewModels
{
    public partial class Histories : ObservableObject
    {
        public Repository Repo => _repo;

        public string SearchText
        {
            get => _searchText;
            set => SetProperty(ref _searchText, value);
        }

        public ObservableCollection<TokenInstance> SearchTokens { get; } = new();

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

        public bool IsAuthorTimeColumnVisible
        {
            get => _repo.UIStates.IsAuthorTimeColumnVisibleInHistory;
            set
            {
                if (_repo.UIStates.IsAuthorTimeColumnVisibleInHistory != value)
                {
                    _repo.UIStates.IsAuthorTimeColumnVisibleInHistory = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool IsCommitTimeColumnVisible
        {
            get => _repo.UIStates.IsCommitTimeColumnVisibleInHistory;
            set
            {
                if (_repo.UIStates.IsCommitTimeColumnVisibleInHistory != value)
                {
                    _repo.UIStates.IsCommitTimeColumnVisibleInHistory = value;
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
            var totalCount = _rawCommits.Count;
            if (totalCount == 0)
            {
                SetProperty(ref _commits, new List<Models.Commit>(), nameof(Commits));
                return;
            }

            BitArray finalBits = new BitArray(totalCount, true);

            // AST-driven token filter via QueryParser + ExprEvaluator (Bitmap Accelerated)
            if (SearchTokens.Count > 0)
            {
                var spec = Controls.QueryParser.Parse(SearchTokens, SearchProviders);

                if (spec.HasSubGroups)
                {
                    // Multiple parenthesized groups: each is AND-ed internally, OR-ed across groups
                    BitArray subGroupsBits = new BitArray(totalCount, false);
                    foreach (var sub in spec.SubGroups)
                    {
                        var r = EvaluateToBitmap(sub);
                        subGroupsBits.Or(r);
                    }
                    finalBits.And(subGroupsBits);
                }
                else
                {
                    // Single group
                    finalBits.And(EvaluateToBitmap(spec));
                }

                _suggestionCache = _cacheManager.Compute(_rawCommits, _commits, spec);
            }
            else
            {
                // No tokens: all caches = _rawCommits
                _suggestionCache = new Dictionary<string, List<Models.Commit>>();
                foreach (var provider in SearchProviders)
                {
                    if (provider.Prefix is "gitlog:")
                        continue;
                    _suggestionCache[provider.Prefix] = _rawCommits;
                }
            }

            // Convert BitArray back to List once
            var processed = new List<Models.Commit>();
            for (int i = 0; i < totalCount; i++)
            {
                if (finalBits[i])
                    processed.Add(_rawCommits[i]);
            }

            var soloTargets = new List<string>(_soloTargets);
            if (SearchTokens.Count > 0)
            {
                var spec = Controls.QueryParser.Parse(SearchTokens, SearchProviders);
                foreach (var group in spec.Groups)
                {
                    if (group.ProviderPrefix == "solo:")
                        ExtractSoloValues(group.Expr, soloTargets);
                }
            }

            processed = FilterCommits(processed, soloTargets);
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
            get => _currentBranch;
            set => SetProperty(ref _currentBranch, value);
        }

        public bool HasSingleRemote
        {
            get => _hasSingleRemote;
            set => SetProperty(ref _hasSingleRemote, value);
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

        public bool IsTerminalView
        {
            get => _isTerminalView;
            set => SetProperty(ref _isTerminalView, value);
        }

        public TerminalViewModel TerminalViewModel { get; }

        public Histories(Repository repo)
        {
            _repo = repo;
            _commitDetailSharedData = new CommitDetailSharedData();
            TerminalViewModel = new TerminalViewModel(repo);

            _repo.UIStates.HistoryFilters.CollectionChanged += (_, e) =>
            {
                if (_suppressHistoryFiltersCollectionChanged)
                    return;

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

            SetupSearchProviders();

            _cacheManager = new SuggestionCacheManager(SearchProviders, (prefix, c, node) => EvalTerm(prefix, c, node));

            SetupSlashCommands();

            SearchTokens.CollectionChanged += OnSearchTokensChanged;

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

            var head = new Commands.QueryRevisionByRefName(_repo.FullPath, "HEAD").GetResult();
            var info = new Models.Bisect();
            var markedHead = false;
            var dir = Path.Combine(_repo.GitDir, "refs", "bisect");
            if (Directory.Exists(dir))
            {
                var files = new DirectoryInfo(dir).GetFiles();
                foreach (var file in files)
                {
                    var sha = File.ReadAllText(file.FullName).Trim();
                    if (!markedHead)
                        markedHead = head.Equals(sha, StringComparison.Ordinal);

                    if (file.Name.StartsWith("bad"))
                        info.Bads.Add(sha);
                    else if (file.Name.StartsWith("good"))
                        info.Goods.Add(sha);
                    else if (file.Name.StartsWith("skip"))
                        info.Skipped.Add(sha);
                }
            }

            Bisect = info;

            if (info.Bads.Count == 0)
                return Models.BisectState.WaitingForFirstBad;

            if (markedHead)
                return Models.BisectState.WaitingForCheckoutAnother;

            if (info.Goods.Count == 0)
                return Models.BisectState.WaitingForFirstGood;

            return Models.BisectState.WaitingForMark;
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

        public void CheckoutCommitDetached(Models.Commit c)
        {
            if (!c.IsCurrentHead && _repo.CanCreatePopup())
                _repo.ShowPopup(new CheckoutDetached(_repo, c));
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

        public async Task CheckoutBranchByCommitAsync(Models.Commit commit, bool isCtrlPressed = false)
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
                {
                    _repo.ShowPopup(new CreateBranch(_repo, firstRemoteBranch));
                }
                else if (!_repo.IsBare)
                {
                    if (isCtrlPressed && _repo.CurrentBranch != null)
                        _repo.ShowPopup(new Reset(_repo, _repo.CurrentBranch, commit));
                    else
                        _repo.ShowPopup(new CheckoutDetached(_repo, commit));
                }
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

        private Models.Commit FindCommitByIdentifier(string target, Dictionary<string, Models.Commit> map)
        {
            if (string.IsNullOrEmpty(target))
                return null;

            if (target.Equals("HEAD", StringComparison.OrdinalIgnoreCase))
                return _rawCommits.Find(x => x.IsCurrentHead);

            if (map.TryGetValue(target, out var exact))
                return exact;

            if (target.Length >= 4 && target.All(char.IsAsciiHexDigit))
            {
                var match = _rawCommits.Find(x => x.SHA.StartsWith(target, StringComparison.OrdinalIgnoreCase));
                if (match != null)
                    return match;
            }

            // Find by branch/tag name in decorators
            return _rawCommits.Find(c => c.Decorators.Any(d => d.Name.Equals(target, StringComparison.OrdinalIgnoreCase)));
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

            var commitMap = new Dictionary<string, int>(commits.Count);
            for (int i = 0; i < commits.Count; i++)
                commitMap[commits[i].SHA] = i;

            var active = new bool[commits.Count];
            foreach (var target in targets)
            {
                var commit = FindCommitByIdentifier(target, rawCommitMap);
                if (commit != null)
                {
                    var lineage = Models.CommitGraph.GetCommitLineageFast(_rawCommits, rawCommitMap, commit, Models.CommitLineageSearchMethod.FullLineage, (uint)_rawCommits.Count);
                    for (int i = 0; i < lineage.Length; i++)
                    {
                        if (lineage[i] && commitMap.TryGetValue(_rawCommits[i].SHA, out var idx))
                            active[idx] = true;
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

            // Re-index for display/folding/graph generation
            for (int i = 0; i < commits.Count; i++)
                commits[i].Index = i;

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
                if (start.HasDecorators || start.Parents.Count != 1 || childrenCount.GetValueOrDefault(start.SHA, 0) > 1 || start.IsCommitFilterHead)
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

        private static readonly Dictionary<string, Controls.ITokenSuggestionProvider> _providersMap = new();

        private static bool EvalTerm(string prefix, Models.Commit c, Controls.ExprNode node)
        {
            var val = node.Value;
            var typedVal = node.TypedValue;

            var provider = _providersMap.GetValueOrDefault(prefix);
            var comp = (provider?.CaseSensitive == true) ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

            return prefix switch
            {
                // [双语注释 / Bilingual Comment]
                // "a:" (作者) 名字比对采用由 suggestion provider 动态配置的 comp 比对模式。
                // "a:" (Author) Name comparison dynamically applies the comp comparison mode resolved from the suggestion provider configuration.
                "a:" => c.Author.Name.Contains(val, comp) ||
                            c.Author.Email.Contains(val, StringComparison.OrdinalIgnoreCase),
                "m:" => (c.Subject ?? string.Empty).Contains(val, StringComparison.OrdinalIgnoreCase),
                "t:" => c.Decorators.Any(d => d.Type == Models.DecoratorType.Tag &&
                                d.Name.Contains(val, StringComparison.OrdinalIgnoreCase)),
                "r:" => c.Decorators.Any(d => d.Type == Models.DecoratorType.RemoteBranchHead &&
                                d.Name.Contains(val, StringComparison.OrdinalIgnoreCase)),
                "s:" => c.SHA.Contains(val, StringComparison.OrdinalIgnoreCase),
                // [双语注释 / Bilingual Comment]
                // "c:" (提交者) 名字比对采用由 suggestion provider 动态配置的 comp 比对模式。
                // "c:" (Committer) Name comparison dynamically applies the comp comparison mode resolved from the suggestion provider configuration.
                "c:" => c.Committer.Name.Contains(val, comp) ||
                            c.Committer.Email.Contains(val, StringComparison.OrdinalIgnoreCase),
                // [双语注释 / Bilingual Comment]
                // "e:" (邮箱) 针对作者及提交者邮箱进行不区分大小写匹配。
                // "e:" (Email) performs case-insensitive match against both author and committer emails.
                "e:" => c.Author.Email.Contains(val, StringComparison.OrdinalIgnoreCase) ||
                            c.Committer.Email.Contains(val, StringComparison.OrdinalIgnoreCase),
                "since:" => (typedVal is DateTimeOffset dtSince ? dtSince : DateTimeOffset.TryParse(val, out var ptSince) ? ptSince : default(DateTimeOffset?)) is DateTimeOffset pds &&
                            DateTimeOffset.FromUnixTimeSeconds((long)c.CommitterTime) >= pds,
                "until:" => (typedVal is DateTimeOffset dtUntil ? dtUntil : DateTimeOffset.TryParse(val, out var ptUntil) ? ptUntil : default(DateTimeOffset?)) is DateTimeOffset pdu &&
                            DateTimeOffset.FromUnixTimeSeconds((long)c.CommitterTime) <= pdu,
                "is:" => MatchesState(val.ToLowerInvariant(), c),
                _ => true,
            };
        }

        private Repository _repo = null;
        private Models.Branch _currentBranch = null;
        private bool _hasSingleRemote = false;
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
        private bool _isTerminalView = false;
        private HashSet<int> _selectedLineagePaths = null;

        private bool[] _selectedLineageCommits = null;
        private int _visibleTopIndex = -1;
        private int _visibleBottomIndex = -1;
        private Dictionary<string, Models.Commit> _commitMap = new();
        private bool _gitOptionsDrivenByTokens = false;
        private bool _suppressSearchTokenCollectionChanged = false;
        private bool _syncingRepoFlagsFromTokens = false;
        private bool _suppressHistoryFiltersCollectionChanged = false;
        private readonly List<Models.HistoryFilter> _searchDrivenFilters = [];
        private bool _suppressGraphRefreshFromSelectionChange = false;
        private int _lineageRequestVersion = 0;
    }
}
