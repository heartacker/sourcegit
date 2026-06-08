using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Avalonia.Collections;
using Avalonia.Threading;

using CommunityToolkit.Mvvm.ComponentModel;

namespace SourceGit.ViewModels
{
    public class Repository : ObservableObject, Models.IRepository
    {
        public bool IsBare
        {
            get;
        }

        public string FullPath
        {
            get;
        }

        public string GitDir
        {
            get;
        }

        public Models.RepositorySettings Settings
        {
            get => _settings;
        }

        public Models.RepositoryUIStates UIStates
        {
            get => _uiStates;
        }

        public Models.GitFlow GitFlow
        {
            get;
            set;
        } = new();

        public Models.FilterMode HistoryFilterMode
        {
            get => _historyFilterMode;
            private set => SetProperty(ref _historyFilterMode, value);
        }

        public bool IsHistoryFiltersCollapsed
        {
            get => _uiStates.IsHistoryFiltersCollapsed;
            set
            {
                if (value != _uiStates.IsHistoryFiltersCollapsed)
                {
                    _uiStates.IsHistoryFiltersCollapsed = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool HasAllowedSignersFile
        {
            get => _hasAllowedSignersFile;
        }

        public int SelectedViewIndex
        {
            get => _selectedViewIndex;
            set
            {
                if (SetProperty(ref _selectedViewIndex, value))
                {
                    OnPropertyChanged(nameof(IsHistoriesVisible));
                    OnPropertyChanged(nameof(IsWorkingCopyVisible));
                    OnPropertyChanged(nameof(IsStashesVisible));
                }
            }
        }

        public Histories Histories
        {
            get => _histories;
        }

        public WorkingCopy WorkingCopy
        {
            get => _workingCopy;
        }

        public TerminalViewModel Terminal
        {
            get
            {
                if (_terminal == null)
                    _terminal = new TerminalViewModel(this);
                return _terminal;
            }
        }

        public StashesPage StashesPage
        {
            get => _stashesPage;
        }

        public bool IsHistoriesVisible
        {
            get => SelectedViewIndex == 0;
        }

        public bool IsWorkingCopyVisible
        {
            get => SelectedViewIndex == 1;
        }

        public bool IsStashesVisible
        {
            get => SelectedViewIndex == 2;
        }

        public bool EnableTopoOrderInHistory
        {
            get => _uiStates.EnableTopoOrderInHistory;
            set
            {
                if (value != _uiStates.EnableTopoOrderInHistory)
                {
                    _uiStates.EnableTopoOrderInHistory = value;
                    RefreshCommits();
                }
            }
        }

        public Models.HistoryShowFlags HistoryShowFlags
        {
            get => _uiStates.HistoryShowFlags;
            set
            {
                if (value != _uiStates.HistoryShowFlags)
                {
                    _uiStates.HistoryShowFlags = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IsReflogEnabled));
                    OnPropertyChanged(nameof(IsFirstParentOnlyEnabled));
                    OnPropertyChanged(nameof(IsSimplifyByDecorationEnabled));
                    RefreshCommits();
                }
            }
        }

        public bool IsReflogEnabled
        {
            get => _uiStates.HistoryShowFlags.HasFlag(Models.HistoryShowFlags.Reflog);
            set => ToggleHistoryShowFlag(Models.HistoryShowFlags.Reflog);
        }

        public bool IsFirstParentOnlyEnabled
        {
            get => _uiStates.HistoryShowFlags.HasFlag(Models.HistoryShowFlags.FirstParentOnly);
            set => ToggleHistoryShowFlag(Models.HistoryShowFlags.FirstParentOnly);
        }

        public bool IsSimplifyByDecorationEnabled
        {
            get => _uiStates.HistoryShowFlags.HasFlag(Models.HistoryShowFlags.SimplifyByDecoration);
            set => ToggleHistoryShowFlag(Models.HistoryShowFlags.SimplifyByDecoration);
        }

        public string Filter
        {
            get => _filter;
            set
            {
                if (SetProperty(ref _filter, value))
                {
                    var builder = BuildBranchTree(_branches, _remotes);
                    LocalBranchTrees = builder.Locals;
                    RemoteBranchTrees = builder.Remotes;
                    VisibleTags = BuildVisibleTags();
                    VisibleSubmodules = BuildVisibleSubmodules();
                }
            }
        }

        public Models.CommitGraphHighlighting GraphHighlighting
        {
            get => _histories.GraphHighlighting;
            set => _histories.GraphHighlighting = value;
        }

        public List<Models.Remote> Remotes
        {
            get => _remotes;
            private set
            {
                if (SetProperty(ref _remotes, value))
                {
                    if (_histories != null)
                        _histories.HasSingleRemote = value != null && value.Count == 1;
                }
            }
        }

        public List<Models.Branch> Branches
        {
            get => _branches;
            private set => SetProperty(ref _branches, value);
        }

        public Models.Branch CurrentBranch
        {
            get => _currentBranch;
            private set
            {
                var oldHead = _currentBranch?.Head;
                if (SetProperty(ref _currentBranch, value))
                {
                    if (_histories != null)
                        _histories.CurrentBranch = value;

                    if (value != null && !value.Head.Equals(oldHead, StringComparison.Ordinal) && _workingCopy is { UseAmend: true })
                        _workingCopy.UseAmend = false;
                }
            }
        }

        public List<BranchTreeNode> LocalBranchTrees
        {
            get => _localBranchTrees;
            private set => SetProperty(ref _localBranchTrees, value);
        }

        public List<BranchTreeNode> RemoteBranchTrees
        {
            get => _remoteBranchTrees;
            private set => SetProperty(ref _remoteBranchTrees, value);
        }

        public List<Worktree> Worktrees
        {
            get => _worktrees;
            private set => SetProperty(ref _worktrees, value);
        }

        public List<Models.Tag> Tags
        {
            get => _tags;
            private set => SetProperty(ref _tags, value);
        }

        public bool ShowTagsAsTree
        {
            get => _uiStates.ShowTagsAsTree;
            set
            {
                if (value != _uiStates.ShowTagsAsTree)
                {
                    _uiStates.ShowTagsAsTree = value;
                    VisibleTags = BuildVisibleTags();
                    OnPropertyChanged();
                }
            }
        }

        public object VisibleTags
        {
            get => _visibleTags;
            private set => SetProperty(ref _visibleTags, value);
        }

        public List<Models.Submodule> Submodules
        {
            get => _submodules;
            private set => SetProperty(ref _submodules, value);
        }

        public bool ShowSubmodulesAsTree
        {
            get => _uiStates.ShowSubmodulesAsTree;
            set
            {
                if (value != _uiStates.ShowSubmodulesAsTree)
                {
                    _uiStates.ShowSubmodulesAsTree = value;
                    VisibleSubmodules = BuildVisibleSubmodules();
                    OnPropertyChanged();
                }
            }
        }

        public object VisibleSubmodules
        {
            get => _visibleSubmodules;
            private set => SetProperty(ref _visibleSubmodules, value);
        }

        public int LocalChangesCount
        {
            get => _localChangesCount;
            private set => SetProperty(ref _localChangesCount, value);
        }

        public int StashesCount
        {
            get => _stashesCount;
            private set => SetProperty(ref _stashesCount, value);
        }

        public int LocalBranchesCount
        {
            get => _localBranchesCount;
            private set => SetProperty(ref _localBranchesCount, value);
        }

        public bool IncludeUntracked
        {
            get => _uiStates.IncludeUntrackedInLocalChanges;
            set
            {
                if (value != _uiStates.IncludeUntrackedInLocalChanges)
                {
                    _uiStates.IncludeUntrackedInLocalChanges = value;
                    OnPropertyChanged();
                    RefreshWorkingCopyChanges();
                }
            }
        }

        public bool IsSearchingCommits
        {
            get => _isSearchingCommits;
            set
            {
                if (SetProperty(ref _isSearchingCommits, value))
                {
                    if (value)
                        SelectedViewIndex = 0;
                    else
                        _searchCommitContext.EndSearch();
                }
            }
        }

        public SearchCommitContext SearchCommitContext
        {
            get => _searchCommitContext;
        }

        public bool IsLocalBranchGroupExpanded
        {
            get => _uiStates.IsLocalBranchesExpandedInSideBar;
            set
            {
                if (value != _uiStates.IsLocalBranchesExpandedInSideBar)
                {
                    _uiStates.IsLocalBranchesExpandedInSideBar = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool IsRemoteGroupExpanded
        {
            get => _uiStates.IsRemotesExpandedInSideBar;
            set
            {
                if (value != _uiStates.IsRemotesExpandedInSideBar)
                {
                    _uiStates.IsRemotesExpandedInSideBar = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool IsTagGroupExpanded
        {
            get => _uiStates.IsTagsExpandedInSideBar;
            set
            {
                if (value != _uiStates.IsTagsExpandedInSideBar)
                {
                    _uiStates.IsTagsExpandedInSideBar = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool IsSubmoduleGroupExpanded
        {
            get => _uiStates.IsSubmodulesExpandedInSideBar;
            set
            {
                if (value != _uiStates.IsSubmodulesExpandedInSideBar)
                {
                    _uiStates.IsSubmodulesExpandedInSideBar = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool IsWorktreeGroupExpanded
        {
            get => _uiStates.IsWorktreeExpandedInSideBar;
            set
            {
                if (value != _uiStates.IsWorktreeExpandedInSideBar)
                {
                    _uiStates.IsWorktreeExpandedInSideBar = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool IsSortingLocalBranchByName
        {
            get => _uiStates.LocalBranchSortMode == Models.BranchSortMode.Name;
            set
            {
                _uiStates.LocalBranchSortMode = value ? Models.BranchSortMode.Name : Models.BranchSortMode.CommitterDate;
                OnPropertyChanged();

                var builder = BuildBranchTree(_branches, _remotes);
                LocalBranchTrees = builder.Locals;
                RemoteBranchTrees = builder.Remotes;
            }
        }

        public bool IsSortingRemoteBranchByName
        {
            get => _uiStates.RemoteBranchSortMode == Models.BranchSortMode.Name;
            set
            {
                _uiStates.RemoteBranchSortMode = value ? Models.BranchSortMode.Name : Models.BranchSortMode.CommitterDate;
                OnPropertyChanged();

                var builder = BuildBranchTree(_branches, _remotes);
                LocalBranchTrees = builder.Locals;
                RemoteBranchTrees = builder.Remotes;
            }
        }

        public bool IsSortingTagsByName
        {
            get => _uiStates.TagSortMode == Models.TagSortMode.Name;
            set
            {
                _uiStates.TagSortMode = value ? Models.TagSortMode.Name : Models.TagSortMode.CreatorDate;
                OnPropertyChanged();
                VisibleTags = BuildVisibleTags();
            }
        }

        public InProgressContext InProgressContext
        {
            get => _workingCopy?.InProgressContext;
        }

        public Models.BisectState BisectState
        {
            get => _bisectState;
            private set => SetProperty(ref _bisectState, value);
        }

        public bool IsBisectCommandRunning
        {
            get => _isBisectCommandRunning;
            private set => SetProperty(ref _isBisectCommandRunning, value);
        }

        public bool IsAutoFetching
        {
            get => _isAutoFetching;
            private set => SetProperty(ref _isAutoFetching, value);
        }

        public AvaloniaList<Models.IssueTracker> IssueTrackers
        {
            get;
        } = [];

        public AvaloniaList<CommandLog> Logs
        {
            get;
        } = [];

        public Repository(bool isBare, string path, string gitDir)
        {
            IsBare = isBare;
            FullPath = path.Replace('\\', '/').TrimEnd('/');
            GitDir = gitDir.Replace('\\', '/').TrimEnd('/');

            var commonDirFile = Path.Combine(GitDir, "commondir");
            var isWorktree = GitDir.IndexOf("/worktrees/", StringComparison.Ordinal) > 0 &&
                          File.Exists(commonDirFile);

            if (isWorktree)
            {
                var commonDir = File.ReadAllText(commonDirFile).Trim();
                if (Path.IsPathRooted(commonDir))
                    commonDir = new DirectoryInfo(commonDir).FullName;
                else
                    commonDir = new DirectoryInfo(Path.Combine(GitDir, commonDir)).FullName;

                _gitCommonDir = commonDir.Replace('\\', '/').TrimEnd('/');
            }
            else
            {
                _gitCommonDir = GitDir;
            }

            _settings = Models.RepositorySettings.Get(_gitCommonDir);
            _uiStates = Models.RepositoryUIStates.Load(GitDir);
        }

        public void Open()
        {
            Preferences.Instance.PropertyChanged += OnPreferencesChanged;
            try
            {
                _watcher = new Models.Watcher(this, FullPath, _gitCommonDir);
            }
            catch (Exception ex)
            {
                SendNotification($"Failed to start watcher for repository: '{FullPath}'. You may need to press 'F5' to refresh repository manually!\n\nReason: {ex.Message}", true);
            }

            _historyFilterMode = _uiStates.GetHistoryFilterMode();
            _histories = new Histories(this);
            SyncSearchTokensFromPersistedHistoryFilters();
            _workingCopy = new WorkingCopy(this) { CommitMessage = _uiStates.LastCommitMessage };
            _stashesPage = new StashesPage(this);
            _searchCommitContext = new SearchCommitContext(this);
            _selectedViewIndex = Preferences.Instance.ShowLocalChangesByDefault ? 1 : 0;
            _lastFetchTime = DateTime.Now;
            _autoFetchTimer = new Timer(AutoFetchByTimer, null, 5000, 5000);
            RefreshAll();
        }

        public void Close()
        {
            Preferences.Instance.PropertyChanged -= OnPreferencesChanged;
            var commitMessage = _workingCopy.CommitMessage;
            if (!string.IsNullOrEmpty(commitMessage) && _workingCopy.InProgressContext != null)
                File.WriteAllText(Path.Combine(GitDir, "MERGE_MSG"), commitMessage);

            _uiStates.LastCommitMessage = commitMessage;
            _terminal?.Dispose();
            _uiStates.Save();

            if (_cancellationRefreshBranches is { IsCancellationRequested: false })
                _cancellationRefreshBranches.Cancel();
            if (_cancellationRefreshTags is { IsCancellationRequested: false })
                _cancellationRefreshTags.Cancel();
            if (_cancellationRefreshWorkingCopyChanges is { IsCancellationRequested: false })
                _cancellationRefreshWorkingCopyChanges.Cancel();
            if (_cancellationRefreshCommits is { IsCancellationRequested: false })
                _cancellationRefreshCommits.Cancel();
            if (_cancellationRefreshStashes is { IsCancellationRequested: false })
                _cancellationRefreshStashes.Cancel();

            _watcher?.Dispose();
            _autoFetchTimer.Dispose();
        }

        private void OnPreferencesChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(Preferences.EnableLinearCommitFolding) || e.PropertyName == nameof(Preferences.MaxLinearCommitsToFold))
            {
                _histories?.UpdateDisplayCommits();
            }
        }

        public void SendNotification(string message, bool isError = false)
        {
            Models.Notification.Send(FullPath, message, isError);
        }

        public bool CanCreatePopup()
        {
            var page = GetOwnerPage();
            if (page == null)
                return false;

            return !_isAutoFetching && page.CanCreatePopup();
        }

        public void ShowPopup(Popup popup)
        {
            var page = GetOwnerPage();
            if (page != null)
                page.Popup = popup;
        }

        public async Task ShowAndStartPopupAsync(Popup popup)
        {
            var page = GetOwnerPage();
            page.Popup = popup;

            if (popup.CanStartDirectly())
                await page.ProcessPopupAsync();
        }

        public bool IsGitFlowEnabled()
        {
            return GitFlow is { IsValid: true } &&
                _branches.Find(x => x.IsLocal && x.Name.Equals(GitFlow.ProductionBranch, StringComparison.Ordinal)) != null &&
                _branches.Find(x => x.IsLocal && x.Name.Equals(GitFlow.DevelopmentBranch, StringComparison.Ordinal)) != null;
        }

        public Models.GitFlowBranchType GetGitFlowType(Models.Branch b)
        {
            if (!IsGitFlowEnabled())
                return Models.GitFlowBranchType.None;

            var name = b.Name;
            if (name.StartsWith(GitFlow.FeaturePrefix, StringComparison.Ordinal))
                return Models.GitFlowBranchType.Feature;
            if (name.StartsWith(GitFlow.ReleasePrefix, StringComparison.Ordinal))
                return Models.GitFlowBranchType.Release;
            if (name.StartsWith(GitFlow.HotfixPrefix, StringComparison.Ordinal))
                return Models.GitFlowBranchType.Hotfix;
            return Models.GitFlowBranchType.None;
        }

        public bool IsLFSEnabled()
        {
            var path = Path.Combine(GitDir, "hooks", "pre-push");
            if (!File.Exists(path))
                return false;

            try
            {
                var content = File.ReadAllText(path);
                return content.Contains("git lfs pre-push");
            }
            catch
            {
                return false;
            }
        }

        public async Task InstallLFSAsync()
        {
            var log = CreateLog("Install LFS");
            var succ = await new Commands.LFS(FullPath).Use(log).InstallAsync();
            if (succ)
                SendNotification("LFS enabled successfully!");

            log.Complete();
        }

        public async Task<bool> TrackLFSFileAsync(string pattern, bool isFilenameMode)
        {
            var log = CreateLog("Track LFS");
            var succ = await new Commands.LFS(FullPath)
                .Use(log)
                .TrackAsync(pattern, isFilenameMode);

            if (succ)
                SendNotification($"Tracking successfully! Pattern: {pattern}");

            log.Complete();
            return succ;
        }

        public async Task<bool> LockLFSFileAsync(string remote, string path)
        {
            var log = CreateLog("Lock LFS File");
            var succ = await new Commands.LFS(FullPath)
                .Use(log)
                .LockAsync(remote, path);

            if (succ)
                SendNotification($"Lock file successfully! File: {path}");

            log.Complete();
            return succ;
        }

        public async Task<bool> UnlockLFSFileAsync(string remote, string path, bool force, bool notify)
        {
            var log = CreateLog("Unlock LFS File");
            var succ = await new Commands.LFS(FullPath)
                .Use(log)
                .UnlockAsync(remote, path, force);

            if (succ && notify)
                SendNotification($"Unlock file successfully! File: {path}");

            log.Complete();
            return succ;
        }

        public CommandLog CreateLog(string name)
        {
            var log = new CommandLog(name);
            Logs.Insert(0, log);
            return log;
        }

        public void RefreshAll()
        {
            RefreshCommits();
            RefreshBranches();
            RefreshTags();
            RefreshSubmodules();
            RefreshWorktrees();
            RefreshWorkingCopyChanges();
            RefreshStashes();

            Task.Run(async () =>
            {
                var issuetrackers = new List<Models.IssueTracker>();
                await new Commands.IssueTracker(FullPath, true).ReadAllAsync(issuetrackers, true).ConfigureAwait(false);
                await new Commands.IssueTracker(FullPath, false).ReadAllAsync(issuetrackers, false).ConfigureAwait(false);
                Dispatcher.UIThread.Post(() =>
                {
                    IssueTrackers.Clear();
                    IssueTrackers.AddRange(issuetrackers);
                });

                var config = await new Commands.Config(FullPath).ReadAllAsync().ConfigureAwait(false);
                _hasAllowedSignersFile = config.TryGetValue("gpg.ssh.allowedsignersfile", out var allowedSignersFile) && !string.IsNullOrEmpty(allowedSignersFile);
                GitFlow.Parse(config);
            });
        }

        public async Task FetchAsync(bool autoStart)
        {
            if (!CanCreatePopup())
                return;

            if (_remotes.Count == 0)
            {
                SendNotification("No remotes added to this repository!!!", true);
                return;
            }

            if (autoStart)
                await ShowAndStartPopupAsync(new Fetch(this));
            else
                ShowPopup(new Fetch(this));
        }

        public async Task PullAsync(bool autoStart)
        {
            if (IsBare || !CanCreatePopup())
                return;

            if (_remotes.Count == 0)
            {
                SendNotification("No remotes added to this repository!!!", true);
                return;
            }

            if (_currentBranch == null)
            {
                SendNotification("Can NOT find current branch!!!", true);
                return;
            }

            var pull = new Pull(this, null);
            if (autoStart && pull.SelectedBranch != null)
                await ShowAndStartPopupAsync(pull);
            else
                ShowPopup(pull);
        }

        public async Task PushAsync(bool autoStart)
        {
            if (!CanCreatePopup())
                return;

            if (_remotes.Count == 0)
            {
                SendNotification("No remotes added to this repository!!!", true);
                return;
            }

            if (_currentBranch == null)
            {
                SendNotification("Can NOT find current branch!!!", true);
                return;
            }

            if (autoStart)
                await ShowAndStartPopupAsync(new Push(this, null));
            else
                ShowPopup(new Push(this, null));
        }

        public void ApplyPatch()
        {
            if (CanCreatePopup())
                ShowPopup(new Apply(this));
        }

        public async Task ExecCustomActionAsync(Models.CustomAction action, object scopeTarget)
        {
            if (!CanCreatePopup())
                return;

            var popup = new ExecuteCustomAction(this, action, scopeTarget);
            if (action.Controls.Count == 0)
                await ShowAndStartPopupAsync(popup);
            else
                ShowPopup(popup);
        }

        public async Task CleanupAsync()
        {
            if (CanCreatePopup())
                await ShowAndStartPopupAsync(new Cleanup(this));
        }

        public void ClearFilter()
        {
            Filter = string.Empty;
        }

        public IDisposable LockWatcher()
        {
            return _watcher?.Lock();
        }

        public void RefreshAfterCreateBranch(Models.Branch created, bool checkout)
        {
            _watcher?.MarkBranchUpdated();
            _watcher?.MarkWorkingCopyUpdated();

            _branches.RemoveAll(b => b.IsLocal && b.Name.Equals(created.Name, StringComparison.Ordinal));
            _branches.Add(created);

            if (checkout)
            {
                if (_currentBranch.IsDetachedHead)
                {
                    _branches.Remove(_currentBranch);
                }
                else
                {
                    _currentBranch.IsCurrent = false;
                    _currentBranch.WorktreePath = null;
                }

                created.IsCurrent = true;
                created.WorktreePath = FullPath;

                var folderEndIdx = created.FullName.LastIndexOf('/');
                if (folderEndIdx > 10)
                    _uiStates.ExpandedBranchNodesInSideBar.Add(created.FullName.Substring(0, folderEndIdx));

                if (_historyFilterMode == Models.FilterMode.Included)
                    SetBranchFilterMode(created, Models.FilterMode.Included, false, false);

                CurrentBranch = created;
            }

            var locals = new List<Models.Branch>();
            var count = 0;
            foreach (var b in _branches)
            {
                if (b.IsLocal)
                {
                    locals.Add(b);
                    if (!b.IsDetachedHead)
                        count++;
                }
            }

            var builder = BuildBranchTree(locals, [], false);
            LocalBranchTrees = builder.Locals;
            LocalBranchesCount = count;

            RefreshCommits();
            RefreshWorkingCopyChanges();
            RefreshWorktrees();
        }

        public void RefreshAfterCheckoutBranch(Models.Branch checkouted)
        {
            _watcher?.MarkBranchUpdated();
            _watcher?.MarkWorkingCopyUpdated();

            if (_currentBranch.IsDetachedHead)
            {
                _branches.Remove(_currentBranch);
            }
            else
            {
                _currentBranch.IsCurrent = false;
                _currentBranch.WorktreePath = null;
            }

            checkouted.IsCurrent = true;
            checkouted.WorktreePath = FullPath;
            if (_historyFilterMode == Models.FilterMode.Included)
                SetBranchFilterMode(checkouted, Models.FilterMode.Included, false, false);

            List<Models.Branch> locals = [];
            foreach (var b in _branches)
            {
                if (b.IsLocal)
                    locals.Add(b);
            }

            var builder = BuildBranchTree(locals, [], false);
            LocalBranchTrees = builder.Locals;
            CurrentBranch = checkouted;

            RefreshCommits();
            RefreshWorkingCopyChanges();
            RefreshWorktrees();
        }

        public void RefreshAfterRenameBranch(Models.Branch b, string newName)
        {
            _watcher?.MarkBranchUpdated();

            var newFullName = $"refs/heads/{newName}";
            _uiStates.RenameBranchFilter(b.FullName, newFullName);

            var renamed = new Models.Branch
            {
                Name = newName,
                FullName = newFullName,
                CommitterDate = b.CommitterDate,
                Head = b.Head,
                IsLocal = b.IsLocal,
                IsCurrent = b.IsCurrent,
                IsDetachedHead = b.IsDetachedHead,
                Upstream = b.Upstream,
                Ahead = b.Ahead,
                Behind = b.Behind,
                Remote = b.Remote,
                IsUpstreamGone = b.IsUpstreamGone,
                WorktreePath = b.WorktreePath,
            };

            _branches.Remove(b);
            _branches.Add(renamed);

            if (b.IsCurrent)
                CurrentBranch = renamed;

            List<Models.Branch> locals = [];
            foreach (var branch in _branches)
            {
                if (branch.IsLocal)
                    locals.Add(branch);
            }

            var builder = BuildBranchTree(locals, [], false);
            LocalBranchTrees = builder.Locals;

            RefreshCommits();
            RefreshWorktrees();
        }

        public void MarkBranchesDirtyManually()
        {
            _watcher?.MarkBranchUpdated();
            RefreshBranches();
            RefreshCommits();
            RefreshWorkingCopyChanges();
            RefreshWorktrees();
        }

        public void MarkTagsDirtyManually()
        {
            _watcher?.MarkTagUpdated();
            RefreshTags();
            RefreshCommits();
        }

        public void MarkWorkingCopyDirtyManually()
        {
            _watcher?.MarkWorkingCopyUpdated();
            RefreshWorkingCopyChanges();
        }

        public void MarkStashesDirtyManually()
        {
            _watcher?.MarkStashUpdated();
            RefreshStashes();
        }

        public void MarkSubmodulesDirtyManually()
        {
            _watcher?.MarkSubmodulesUpdated();
            RefreshSubmodules();
        }

        public void MarkFetched()
        {
            _lastFetchTime = DateTime.Now;
        }

        public void NavigateToCommit(string sha, bool isDelayMode = false)
        {
            if (isDelayMode)
            {
                _navigateToCommitDelayed = sha;
            }
            else
            {
                SelectedViewIndex = 0;
                if (sha == "HEAD")
                    sha = _currentBranch.Head;
                _histories?.NavigateTo(sha);
            }
        }

        public void NavigateToBranch(string branch, bool isDelayMode = false)
        {
            var b = _branches.Find(b =>
                b.FullName.Equals(branch, StringComparison.Ordinal) ||
                b.Name.Equals(branch, StringComparison.Ordinal) ||
                (!b.IsLocal && b.FriendlyName.Equals(branch, StringComparison.Ordinal)));
            if (b != null)
                NavigateToCommit(b.Head);
        }

        public void NavigateToTag(string tag, bool isDelayMode = false)
        {
            var t = _tags.Find(t => t.Name.Equals(tag, StringComparison.Ordinal));
            if (t != null)
                NavigateToCommit(t.SHA);
        }

        public void SetCommitMessage(string message)
        {
            if (_workingCopy is not null)
                _workingCopy.CommitMessage = message;
        }

        public void ClearCommitMessage()
        {
            if (_workingCopy is not null)
                _workingCopy.CommitMessage = string.Empty;
        }

        public Models.Commit GetSelectedCommitInHistory()
        {
            return (_histories?.DetailContext as CommitDetail)?.Commit;
        }

        public void ClearHistoryFilters()
        {
            _uiStates.HistoryFilters.Clear();
            if (_histories != null)
            {
                RemoveSearchTokensByPrefix("branch:");
                RemoveSearchTokensByPrefix("b:");
                RemoveSearchTokensByPrefix("remote:");
                RemoveSearchTokensByPrefix("r:");
                RemoveSearchTokensByPrefix("tag:");
                RemoveSearchTokensByPrefix("t:");
            }

            HistoryFilterMode = Models.FilterMode.None;
            ResetBranchTreeFilterMode(LocalBranchTrees);
            ResetBranchTreeFilterMode(RemoteBranchTrees);
            ResetTagFilterMode();
        }

        public void RemoveHistoryFilter(Models.HistoryFilter filter)
        {
            if (_uiStates.HistoryFilters.Remove(filter))
            {
                RemoveSearchTokensForHistoryFilter(filter);
                RefreshHistoryFilters(true);
            }
        }

        public void UpdateBranchNodeIsExpanded(BranchTreeNode node)
        {
            if (_uiStates == null || !string.IsNullOrWhiteSpace(_filter))
                return;

            if (node.IsExpanded)
            {
                if (!_uiStates.ExpandedBranchNodesInSideBar.Contains(node.Path))
                    _uiStates.ExpandedBranchNodesInSideBar.Add(node.Path);
            }
            else
            {
                _uiStates.ExpandedBranchNodesInSideBar.Remove(node.Path);
            }
        }

        public void SetTagFilterMode(Models.Tag tag, Models.FilterMode mode)
        {
            var token = $"t:{tag.Name}";
            var negToken = $"-t:{tag.Name}";

            if (mode == Models.FilterMode.Included)
            {
                if (HistoryFilterMode == Models.FilterMode.Excluded)
                    ClearHistoryFilters();

                RemoveSearchTokenIgnoreCase(negToken);
                AddSearchTokenIfMissing(token);
            }
            else if (mode == Models.FilterMode.Excluded)
            {
                if (HistoryFilterMode == Models.FilterMode.Included)
                    ClearHistoryFilters();

                RemoveSearchTokenIgnoreCase(token);
                AddSearchTokenIfMissing(negToken);
            }
            else
            {
                RemoveSearchTokenIgnoreCase(token);
                RemoveSearchTokenIgnoreCase(negToken);
            }

            _uiStates.UpdateHistoryFilters(tag.Name, Models.FilterType.Tag, mode);

            RefreshHistoryFilters(true);
        }

        public Models.FilterMode GetTagFilterMode(Models.Tag tag)
        {
            return tag == null ? Models.FilterMode.None : GetTagFilterMode(tag.Name);
        }

        public Models.FilterMode GetBranchFilterMode(Models.Branch branch)
        {
            if (branch == null || _histories == null)
                return Models.FilterMode.None;

            var names = new List<string>() { branch.IsLocal ? branch.Name : branch.FriendlyName };
            bool includes = false;
            bool excludes = false;
            foreach (var token in _histories.SearchTokens)
            {
                var neg = token.Raw.StartsWith("-", StringComparison.Ordinal);
                var check = neg ? token.Raw[1..] : token.Raw;

                bool match = false;
                if (branch.IsLocal && (check.StartsWith("branch:", StringComparison.OrdinalIgnoreCase) || check.StartsWith("b:", StringComparison.OrdinalIgnoreCase)))
                {
                    var value = check.Substring(check.IndexOf(':') + 1).Trim();
                    match = names.Any(n => n.Contains(value, StringComparison.OrdinalIgnoreCase));
                }
                else if (!branch.IsLocal && (check.StartsWith("remote:", StringComparison.OrdinalIgnoreCase) || check.StartsWith("r:", StringComparison.OrdinalIgnoreCase)))
                {
                    var value = check.Substring(check.IndexOf(':') + 1).Trim();
                    match = names.Any(n => n.Contains(value, StringComparison.OrdinalIgnoreCase));
                }

                if (match)
                {
                    if (neg)
                        excludes = true;
                    else
                        includes = true;
                }
            }

            if (includes)
                return Models.FilterMode.Included;
            if (excludes)
                return Models.FilterMode.Excluded;
            return Models.FilterMode.None;
        }

        public void SetBranchFilterMode(Models.Branch branch, Models.FilterMode mode, bool clearExists, bool refresh)
        {
            var node = FindBranchNode(branch.IsLocal ? _localBranchTrees : _remoteBranchTrees, branch.FullName);
            if (node != null)
                SetBranchFilterMode(node, mode, clearExists, refresh);
        }

        public void SetBranchFilterMode(BranchTreeNode node, Models.FilterMode mode, bool clearExists, bool refresh)
        {
            var isLocal = node.Path.StartsWith("refs/heads/", StringComparison.Ordinal);
            var tree = isLocal ? _localBranchTrees : _remoteBranchTrees;
            var tokenPrefix = isLocal ? "b:" : "r:";
            var tokenValue = node.Backend is Models.Branch b
                ? (isLocal ? b.Name : b.FriendlyName)
                : (isLocal ? node.Path.Substring("refs/heads/".Length) : node.Path.Substring("refs/remotes/".Length));

            var token = $"{tokenPrefix}{tokenValue}";
            var negToken = $"-{tokenPrefix}{tokenValue}";

            if (clearExists)
                ClearHistoryFilters();

            if (node.Backend is Models.Branch branch)
            {
                var type = isLocal ? Models.FilterType.LocalBranch : Models.FilterType.RemoteBranch;
                _uiStates.UpdateHistoryFilters(node.Path, type, mode);

                if (isLocal && !string.IsNullOrEmpty(branch.Upstream) && !branch.IsUpstreamGone)
                    _uiStates.UpdateHistoryFilters(branch.Upstream, Models.FilterType.RemoteBranch, mode);
            }
            else
            {
                var type = isLocal ? Models.FilterType.LocalBranchFolder : Models.FilterType.RemoteBranchFolder;
                _uiStates.UpdateHistoryFilters(node.Path, type, mode);
                _uiStates.RemoveBranchFiltersByPrefix(node.Path);
            }

            var parentType = isLocal ? Models.FilterType.LocalBranchFolder : Models.FilterType.RemoteBranchFolder;
            var cur = node;
            do
            {
                var lastSepIdx = cur.Path.LastIndexOf('/');
                if (lastSepIdx <= 0)
                    break;

                var parentPath = cur.Path.Substring(0, lastSepIdx);
                var parent = FindBranchNode(tree, parentPath);
                if (parent == null)
                    break;

                _uiStates.UpdateHistoryFilters(parent.Path, parentType, Models.FilterMode.None);
                cur = parent;
            } while (true);

            if (mode == Models.FilterMode.Included)
            {
                if (HistoryFilterMode == Models.FilterMode.Excluded)
                    ClearHistoryFilters();

                RemoveSearchTokenIgnoreCase(negToken);
                AddSearchTokenIfMissing(token);
            }
            else if (mode == Models.FilterMode.Excluded)
            {
                if (HistoryFilterMode == Models.FilterMode.Included)
                    ClearHistoryFilters();

                RemoveSearchTokenIgnoreCase(token);
                AddSearchTokenIfMissing(negToken);
            }
            else
            {
                RemoveSearchTokenIgnoreCase(token);
                RemoveSearchTokenIgnoreCase(negToken);
            }

            RefreshHistoryFilters(refresh);
        }

        public async Task StashAllAsync(bool autoStart)
        {
            if (!CanCreatePopup())
                return;

            var popup = new StashChanges(this, null);
            if (autoStart)
                await ShowAndStartPopupAsync(popup);
            else
                ShowPopup(popup);
        }

        public async Task SkipMergeAsync()
        {
            if (_workingCopy != null)
                await _workingCopy.SkipMergeAsync();
        }

        public async Task AbortMergeAsync()
        {
            if (_workingCopy != null)
                await _workingCopy.AbortMergeAsync();
        }

        public List<(Models.CustomAction, CustomActionContextMenuLabel)> GetCustomActions(Models.CustomActionScope scope)
        {
            var actions = new List<(Models.CustomAction, CustomActionContextMenuLabel)>();

            foreach (var act in Preferences.Instance.CustomActions)
            {
                if (act.Scope == scope)
                    actions.Add((act, new CustomActionContextMenuLabel(act.Name, true)));
            }

            foreach (var act in _settings.CustomActions)
            {
                if (act.Scope == scope)
                    actions.Add((act, new CustomActionContextMenuLabel(act.Name, false)));
            }

            return actions;
        }

        public async Task ExecBisectCommandAsync(string subcmd)
        {
            using var lockWatcher = _watcher?.Lock();
            IsBisectCommandRunning = true;

            var log = CreateLog($"Bisect({subcmd})");

            var succ = await new Commands.Bisect(FullPath, subcmd).Use(log).ExecAsync();
            log.Complete();

            var head = await new Commands.QueryRevisionByRefName(FullPath, "HEAD").GetResultAsync();
            if (!succ)
                SendNotification(log.Content.Substring(log.Content.IndexOf('\n')).Trim(), true);
            else if (log.Content.Contains("is the first bad commit"))
                SendNotification(log.Content.Substring(log.Content.IndexOf('\n')).Trim());

            MarkBranchesDirtyManually();
            NavigateToCommit(head, true);
            IsBisectCommandRunning = false;
        }

        public bool MayHaveSubmodules()
        {
            var modulesFile = Path.Combine(FullPath, ".gitmodules");
            var info = new FileInfo(modulesFile);
            return info.Exists && info.Length > 20;
        }

        public void RefreshBranches()
        {
            if (_cancellationRefreshBranches is { IsCancellationRequested: false })
                _cancellationRefreshBranches.Cancel();

            _cancellationRefreshBranches = new CancellationTokenSource();
            var token = _cancellationRefreshBranches.Token;

            Task.Run(async () =>
            {
                var branches = await new Commands.QueryBranches(FullPath).GetResultAsync().ConfigureAwait(false);
                var remotes = await new Commands.QueryRemotes(FullPath).GetResultAsync().ConfigureAwait(false);
                var builder = BuildBranchTree(branches, remotes);

                Dispatcher.UIThread.Invoke(() =>
                {
                    if (token.IsCancellationRequested)
                        return;

                    Remotes = remotes;
                    Branches = branches;
                    CurrentBranch = branches.Find(x => x.IsCurrent);
                    LocalBranchTrees = builder.Locals;
                    RemoteBranchTrees = builder.Remotes;

                    ValidateHistoryFilters(true);

                    var localBranchesCount = 0;
                    foreach (var b in branches)
                    {
                        if (b.IsLocal && !b.IsDetachedHead)
                            localBranchesCount++;
                    }
                    LocalBranchesCount = localBranchesCount;

                    if (_workingCopy != null)
                        _workingCopy.HasRemotes = remotes.Count > 0;

                    var hasPendingPullOrPush = CurrentBranch?.IsTrackStatusVisible ?? false;
                    GetOwnerPage()?.ChangeDirtyState(Models.DirtyState.HasPendingPullOrPush, !hasPendingPullOrPush);
                });
            }, token);
        }

        public void RefreshWorktrees()
        {
            Task.Run(async () =>
            {
                var worktrees = await new Commands.Worktree(FullPath).ReadAllAsync().ConfigureAwait(false);
                var cleaned = Worktree.Build(FullPath, worktrees);
                Dispatcher.UIThread.Invoke(() => Worktrees = cleaned);
            });
        }

        public void RefreshTags()
        {
            if (_cancellationRefreshTags is { IsCancellationRequested: false })
                _cancellationRefreshTags.Cancel();

            _cancellationRefreshTags = new CancellationTokenSource();
            var token = _cancellationRefreshTags.Token;

            Task.Run(async () =>
            {
                var tags = await new Commands.QueryTags(FullPath).GetResultAsync().ConfigureAwait(false);
                Dispatcher.UIThread.Invoke(() =>
                {
                    if (token.IsCancellationRequested)
                        return;

                    Tags = tags;
                    VisibleTags = BuildVisibleTags();
                    ValidateHistoryFilters(false);
                });
            }, token);
        }

        public List<string> SoloTargets => _histories.SoloTargets;

        public void ClearSoloMode()
        {
            _histories.SoloTargets = [];
        }

        public void SetSoloCommitFilterMode(Models.Commit commit, Models.FilterMode mode)
        {
            var targets = new List<string>(_histories.SoloTargets);
            if (mode == Models.FilterMode.Included)
            {
                if (!targets.Contains(commit.SHA))
                    targets.Add(commit.SHA);
            }
            else
            {
                targets.Remove(commit.SHA);
            }

            _histories.SoloTargets = targets;
        }

        public void SetSoloCommitFilterMode(IEnumerable<Models.Commit> commits, Models.FilterMode mode)
        {
            var targets = new List<string>(_histories.SoloTargets);
            if (mode == Models.FilterMode.Included)
            {
                foreach (var c in commits)
                {
                    if (!targets.Contains(c.SHA))
                        targets.Add(c.SHA);
                }
            }
            else
            {
                foreach (var c in commits)
                    targets.Remove(c.SHA);
            }

            _histories.SoloTargets = targets;
        }

        public void SetSoloCommitFilterMode(string sha, Models.FilterMode mode)
        {
            var targets = new List<string>(_histories.SoloTargets);
            if (mode == Models.FilterMode.Included)
            {
                if (!targets.Contains(sha))
                    targets.Add(sha);
            }
            else
            {
                targets.Remove(sha);
            }

            _histories.SoloTargets = targets;
        }

        public void SetSoloCommitFilterMode(IEnumerable<string> shas, Models.FilterMode mode)
        {
            var targets = new List<string>(_histories.SoloTargets);
            if (mode == Models.FilterMode.Included)
            {
                foreach (var sha in shas)
                {
                    if (!targets.Contains(sha))
                        targets.Add(sha);
                }
            }
            else
            {
                foreach (var sha in shas)
                    targets.Remove(sha);
            }

            _histories.SoloTargets = targets;
        }

        public void RefreshCommits()
        {
            if (_cancellationRefreshCommits is { IsCancellationRequested: false })
                _cancellationRefreshCommits.Cancel();

            _cancellationRefreshCommits = new CancellationTokenSource();
            var token = _cancellationRefreshCommits.Token;

            Task.Run(async () =>
            {
                await Dispatcher.UIThread.InvokeAsync(() => _histories.IsLoading = true);

                var builder = new StringBuilder();
                builder
                    .Append('-').Append(Preferences.Instance.MaxHistoryCommits).Append(' ')
                    .Append(_uiStates.BuildHistoryParams(GitDir));

                var commits = await new Commands.QueryCommits(FullPath, builder.ToString())
                    .GetResultAsync()
                    .ConfigureAwait(false);

                var merged = new HashSet<string>();
                foreach (var c in commits)
                {
                    if (merged.Remove(c.SHA))
                        c.IsMerged = true;

                    if (c.IsMerged)
                    {
                        foreach (var p in c.Parents)
                            merged.Add(p);
                    }
                }

                Dispatcher.UIThread.Invoke(() =>
                {
                    if (token.IsCancellationRequested)
                        return;

                    if (_histories != null)
                    {
                        _histories.IsLoading = false;
                        _histories.Commits = commits;
                        BisectState = _histories.UpdateBisectInfo();

                        if (!string.IsNullOrEmpty(_navigateToCommitDelayed))
                            NavigateToCommit(_navigateToCommitDelayed);
                    }

                    _navigateToCommitDelayed = string.Empty;
                });
            }, token);
        }

        public void RefreshSubmodules()
        {
            if (!MayHaveSubmodules())
            {
                if (_submodules.Count > 0)
                {
                    Dispatcher.UIThread.Invoke(() =>
                    {
                        Submodules = [];
                        VisibleSubmodules = BuildVisibleSubmodules();
                    });
                }

                return;
            }

            Task.Run(async () =>
            {
                var submodules = await new Commands.QuerySubmodules(FullPath).GetResultAsync().ConfigureAwait(false);

                Dispatcher.UIThread.Invoke(() =>
                {
                    bool hasChanged = _submodules.Count != submodules.Count;
                    if (!hasChanged)
                    {
                        var old = new Dictionary<string, Models.Submodule>();
                        foreach (var module in _submodules)
                            old.Add(module.Path, module);

                        foreach (var module in submodules)
                        {
                            if (!old.TryGetValue(module.Path, out var exist))
                            {
                                hasChanged = true;
                                break;
                            }

                            hasChanged = !exist.SHA.Equals(module.SHA, StringComparison.Ordinal) ||
                                         !exist.Branch.Equals(module.Branch, StringComparison.Ordinal) ||
                                         !exist.URL.Equals(module.URL, StringComparison.Ordinal) ||
                                         exist.Status != module.Status;

                            if (hasChanged)
                                break;
                        }
                    }

                    if (hasChanged)
                    {
                        Submodules = submodules;
                        VisibleSubmodules = BuildVisibleSubmodules();
                    }
                });
            });
        }

        public void RefreshWorkingCopyChanges()
        {
            if (IsBare)
                return;

            if (_cancellationRefreshWorkingCopyChanges is { IsCancellationRequested: false })
                _cancellationRefreshWorkingCopyChanges.Cancel();

            _cancellationRefreshWorkingCopyChanges = new CancellationTokenSource();
            var token = _cancellationRefreshWorkingCopyChanges.Token;
            var noOptionalLocks = Interlocked.Add(ref _queryLocalChangesTimes, 1) > 1;

            Task.Run(async () =>
            {
                var changes = await new Commands.QueryLocalChanges(FullPath, _uiStates.IncludeUntrackedInLocalChanges, noOptionalLocks)
                    .WithCancellation(token)
                    .GetResultAsync()
                    .ConfigureAwait(false);

                changes.Sort((l, r) => Models.NumericSort.Compare(l.Path, r.Path));

                Dispatcher.UIThread.Invoke(() =>
                {
                    if (token.IsCancellationRequested)
                        return;

                    _workingCopy.SetData(changes);
                    LocalChangesCount = changes.Count;
                    OnPropertyChanged(nameof(InProgressContext));
                    GetOwnerPage()?.ChangeDirtyState(Models.DirtyState.HasLocalChanges, changes.Count == 0);
                });
            }, token);
        }

        public void RefreshStashes()
        {
            if (IsBare)
                return;

            if (_cancellationRefreshStashes is { IsCancellationRequested: false })
                _cancellationRefreshStashes.Cancel();

            _cancellationRefreshStashes = new CancellationTokenSource();
            var token = _cancellationRefreshStashes.Token;

            Task.Run(async () =>
            {
                var stashes = await new Commands.QueryStashes(FullPath).GetResultAsync().ConfigureAwait(false);
                Dispatcher.UIThread.Invoke(() =>
                {
                    if (token.IsCancellationRequested)
                        return;

                    if (_stashesPage != null)
                        _stashesPage.Stashes = stashes;

                    StashesCount = stashes.Count;
                });
            }, token);
        }

        public void ToggleHistoryShowFlag(Models.HistoryShowFlags flag)
        {
            if (_uiStates.HistoryShowFlags.HasFlag(flag))
                HistoryShowFlags -= flag;
            else
                HistoryShowFlags |= flag;
        }

        public void CreateNewBranch()
        {
            if (_currentBranch == null)
            {
                SendNotification("Git cannot create a branch before your first commit.", true);
                return;
            }

            if (CanCreatePopup())
                ShowPopup(new CreateBranch(this, _currentBranch));
        }

        public async Task CheckoutBranchAsync(Models.Branch branch)
        {
            if (branch.IsLocal)
            {
                var worktree = _worktrees.Find(x => x.IsAttachedTo(branch));
                if (worktree != null)
                {
                    OpenWorktree(worktree);
                    return;
                }
            }

            if (IsBare)
                return;

            if (!CanCreatePopup())
                return;

            if (branch.IsLocal)
            {
                if (_workingCopy is { CanSwitchBranchDirectly: true })
                    await ShowAndStartPopupAsync(new Checkout(this, branch));
                else
                    ShowPopup(new Checkout(this, branch));
            }
            else
            {
                foreach (var b in _branches)
                {
                    if (b.IsLocal &&
                        !string.IsNullOrEmpty(b.Upstream) &&
                        b.Upstream.Equals(branch.FullName, StringComparison.Ordinal) &&
                        b.Ahead.Count == 0)
                    {
                        if (b.Behind.Count > 0)
                            ShowPopup(new CheckoutAndFastForward(this, b, branch));
                        else if (!b.IsCurrent)
                            await CheckoutBranchAsync(b);

                        return;
                    }
                }

                ShowPopup(new CreateBranch(this, branch));
            }
        }

        public async Task CheckoutTagAsync(Models.Tag tag)
        {
            var c = await new Commands.QuerySingleCommit(FullPath, tag.SHA).GetResultAsync();
            if (c != null && _histories != null)
                await _histories.CheckoutBranchByCommitAsync(c);
        }

        public void DeleteBranch(Models.Branch branch)
        {
            if (CanCreatePopup())
                ShowPopup(new DeleteBranch(this, branch));
        }

        public void DeleteMultipleBranches(List<Models.Branch> branches, bool isLocal)
        {
            if (CanCreatePopup())
                ShowPopup(new DeleteMultipleBranches(this, branches, isLocal));
        }

        public void MergeMultipleBranches(List<Models.Branch> branches)
        {
            if (CanCreatePopup())
                ShowPopup(new MergeMultiple(this, branches));
        }

        public void CreateNewTag()
        {
            if (_currentBranch == null)
            {
                SendNotification("Git cannot create a tag before your first commit.", true);
                return;
            }

            if (CanCreatePopup())
                ShowPopup(new CreateTag(this, _currentBranch));
        }

        public void DeleteTag(Models.Tag tag)
        {
            if (CanCreatePopup())
                ShowPopup(new DeleteTag(this, tag));
        }

        public void AddRemote()
        {
            if (CanCreatePopup())
                ShowPopup(new AddRemote(this));
        }

        public void DeleteRemote(Models.Remote remote)
        {
            if (CanCreatePopup())
                ShowPopup(new DeleteRemote(this, remote));
        }

        public async Task ToggleAutoFetchOnRemoteAsync(Models.Remote remote)
        {
            var val = remote.DisableAutoFetch ? "false" : "true";
            var succ = await new Commands.Config(FullPath).SetAsync($"remote.{remote.Name.Quoted()}.disableautofetch", val);
            if (succ)
                remote.DisableAutoFetch = !remote.DisableAutoFetch;
        }

        public void AddSubmodule()
        {
            if (CanCreatePopup())
                ShowPopup(new AddSubmodule(this));
        }

        public void UpdateSubmodules()
        {
            if (CanCreatePopup())
                ShowPopup(new UpdateSubmodules(this, null));
        }

        public async Task AutoUpdateSubmodulesAsync(Models.ICommandLog log)
        {
            var submodules = await new Commands.QueryUpdatableSubmodules(FullPath, false).GetResultAsync();
            if (submodules.Count == 0)
                return;

            do
            {
                if (_settings.AskBeforeAutoUpdatingSubmodules)
                {
                    var builder = new StringBuilder();
                    builder.Append("\n\n");
                    foreach (var s in submodules)
                        builder.Append("- ").Append(s).Append('\n');
                    builder.Append("\n");

                    var msg = App.Text("Checkout.WarnUpdatingSubmodules", builder.ToString());
                    var shouldContinue = await App.AskConfirmAsync(msg, Models.ConfirmButtonType.YesNo);
                    if (!shouldContinue)
                        break;
                }

                await new Commands.Submodule(FullPath)
                    .Use(log)
                    .UpdateAsync(submodules, false, _settings.EnableRecursiveWhenAutoUpdatingSubmodules, false);
            } while (false);
        }

        public void OpenSubmodule(string submodule)
        {
            var selfPage = GetOwnerPage();
            if (selfPage == null)
                return;

            var fullpath = Path.GetFullPath(Path.Combine(FullPath, submodule));
            App.GetLauncher().OpenSubRepository(selfPage, fullpath);
        }

        public void AddWorktree()
        {
            if (CanCreatePopup())
                ShowPopup(new AddWorktree(this));
        }

        public async Task PruneWorktreesAsync()
        {
            if (CanCreatePopup())
                await ShowAndStartPopupAsync(new PruneWorktrees(this));
        }

        public void OpenWorktree(Worktree worktree)
        {
            if (worktree.IsCurrent)
                return;

            var normalizedPath = worktree.FullPath.Replace('\\', '/').TrimEnd('/');
            App.GetLauncher().OpenRepositoryInTab(normalizedPath, null);
        }

        public async Task LockWorktreeAsync(Worktree worktree)
        {
            using var lockWatcher = _watcher?.Lock();
            var log = CreateLog("Lock Worktree");
            var succ = await new Commands.Worktree(FullPath).Use(log).LockAsync(worktree.FullPath);
            if (succ)
                worktree.IsLocked = true;
            log.Complete();
        }

        public async Task UnlockWorktreeAsync(Worktree worktree)
        {
            using var lockWatcher = _watcher?.Lock();
            var log = CreateLog("Unlock Worktree");
            var succ = await new Commands.Worktree(FullPath).Use(log).UnlockAsync(worktree.FullPath);
            if (succ)
                worktree.IsLocked = false;
            log.Complete();
        }

        public List<AI.Service> GetPreferredOpenAIServices()
        {
            var services = Preferences.Instance.OpenAIServices;
            if (services == null || services.Count == 0)
                return [];

            if (services.Count == 1)
                return [services[0]];

            var preferred = _settings.PreferredOpenAIService;
            var all = new List<AI.Service>();
            foreach (var service in services)
            {
                if (service.Name.Equals(preferred, StringComparison.Ordinal))
                    return [service];

                all.Add(service);
            }

            return all;
        }

        public void DiscardAllChanges()
        {
            if (CanCreatePopup())
                ShowPopup(new Discard(this));
        }

        public void ClearStashes()
        {
            if (CanCreatePopup())
                ShowPopup(new ClearStashes(this));
        }

        public async Task<bool> SaveCommitAsPatchAsync(Models.Commit commit, string folder, int index = 0)
        {
            var ignoredChars = new HashSet<char> { '/', '\\', ':', ',', '*', '?', '\"', '<', '>', '|', '`', '$', '^', '%', '[', ']', '+', '-' };
            var builder = new StringBuilder();
            builder.Append(index.ToString("D4"));
            builder.Append('-');

            var chars = commit.Subject.ToCharArray();
            var len = 0;
            foreach (var c in chars)
            {
                if (!ignoredChars.Contains(c))
                {
                    if (c == ' ' || c == '\t')
                        builder.Append('-');
                    else
                        builder.Append(c);

                    len++;

                    if (len >= 48)
                        break;
                }
            }
            builder.Append(".patch");

            var saveTo = Path.Combine(folder, builder.ToString());
            var log = CreateLog("Save Commit as Patch");
            var succ = await new Commands.FormatPatch(FullPath, commit.SHA, saveTo).Use(log).ExecAsync();
            log.Complete();
            return succ;
        }

        private LauncherPage GetOwnerPage()
        {
            var launcher = App.GetLauncher();
            if (launcher == null)
                return null;

            foreach (var page in launcher.Pages)
            {
                if (page.Node.Id.Equals(FullPath))
                    return page;
            }

            return null;
        }

        private BranchTreeNode.Builder BuildBranchTree(List<Models.Branch> branches, List<Models.Remote> remotes, bool validateExpandedNodes = true)
        {
            var builder = new BranchTreeNode.Builder(_uiStates.LocalBranchSortMode, _uiStates.RemoteBranchSortMode);
            if (string.IsNullOrEmpty(_filter))
            {
                builder.SetExpandedNodes(_uiStates.ExpandedBranchNodesInSideBar);
                builder.Run(branches, remotes, false);

                if (validateExpandedNodes)
                {
                    foreach (var invalid in builder.InvalidExpandedNodes)
                        _uiStates.ExpandedBranchNodesInSideBar.Remove(invalid);
                }
            }
            else
            {
                var visibles = new List<Models.Branch>();
                foreach (var b in branches)
                {
                    if (b.FullName.Contains(_filter, StringComparison.OrdinalIgnoreCase))
                        visibles.Add(b);
                }

                builder.Run(visibles, remotes, true);
            }

            UpdateBranchTreeFilterMode(builder.Locals);
            UpdateBranchTreeFilterMode(builder.Remotes);
            return builder;
        }

        private object BuildVisibleTags()
        {
            switch (_uiStates.TagSortMode)
            {
                case Models.TagSortMode.CreatorDate:
                    _tags.Sort((l, r) => r.CreatorDate.CompareTo(l.CreatorDate));
                    break;
                default:
                    _tags.Sort((l, r) => Models.NumericSort.Compare(l.Name, r.Name));
                    break;
            }

            var visible = new List<Models.Tag>();
            if (string.IsNullOrEmpty(_filter))
            {
                visible.AddRange(_tags);
            }
            else
            {
                foreach (var t in _tags)
                {
                    if (t.Name.Contains(_filter, StringComparison.OrdinalIgnoreCase))
                        visible.Add(t);
                }
            }

            if (_uiStates.ShowTagsAsTree)
            {
                var tree = TagCollectionAsTree.Build(visible, _visibleTags as TagCollectionAsTree);
                foreach (var node in tree.Tree)
                    UpdateTagTreeFilterMode(node);
                return tree;
            }
            else
            {
                var list = new TagCollectionAsList(visible);
                foreach (var item in list.TagItems)
                    item.FilterMode = GetTagFilterMode(item.Tag.Name);
                return list;
            }
        }

        private object BuildVisibleSubmodules()
        {
            var visible = new List<Models.Submodule>();
            if (string.IsNullOrEmpty(_filter))
            {
                visible.AddRange(_submodules);
            }
            else
            {
                foreach (var s in _submodules)
                {
                    if (s.Path.Contains(_filter, StringComparison.OrdinalIgnoreCase))
                        visible.Add(s);
                }
            }

            if (_uiStates.ShowSubmodulesAsTree)
                return SubmoduleCollectionAsTree.Build(visible, _visibleSubmodules as SubmoduleCollectionAsTree);
            else
                return new SubmoduleCollectionAsList() { Submodules = visible };
        }

        private void RefreshHistoryFilters(bool refresh)
        {
            HistoryFilterMode = GetTokenFilterMode();
            if (!refresh)
                return;

            UpdateBranchTreeFilterMode(LocalBranchTrees);
            UpdateBranchTreeFilterMode(RemoteBranchTrees);
            UpdateTagFilterMode();
        }

        private void UpdateBranchTreeFilterMode(List<BranchTreeNode> nodes)
        {
            foreach (var node in nodes)
            {
                node.FilterMode = GetBranchNodeFilterMode(node);

                if (!node.IsBranch)
                    UpdateBranchTreeFilterMode(node.Children);
            }
        }

        private void UpdateTagFilterMode()
        {
            if (VisibleTags is TagCollectionAsTree tree)
            {
                foreach (var node in tree.Tree)
                    UpdateTagTreeFilterMode(node);
            }
            else if (VisibleTags is TagCollectionAsList list)
            {
                foreach (var item in list.TagItems)
                    item.FilterMode = GetTagFilterMode(item.Tag.Name);
            }
        }

        private void UpdateTagTreeFilterMode(TagTreeNode node)
        {
            if (node.IsFolder)
            {
                foreach (var child in node.Children)
                    UpdateTagTreeFilterMode(child);

                var include = node.Children.Any(c => c.FilterMode == Models.FilterMode.Included);
                var exclude = node.Children.Any(c => c.FilterMode == Models.FilterMode.Excluded);
                node.FilterMode = include ? Models.FilterMode.Included : (exclude ? Models.FilterMode.Excluded : Models.FilterMode.None);
            }
            else
            {
                node.FilterMode = GetTagFilterMode(node.FullPath);
            }
        }

        private Models.FilterMode GetTokenFilterMode()
        {
            if (_histories == null)
                return Models.FilterMode.None;

            var hasIncluded = _histories.SearchTokens.Any(t =>
                t.Raw.StartsWith("branch:", StringComparison.OrdinalIgnoreCase) ||
                t.Raw.StartsWith("b:", StringComparison.OrdinalIgnoreCase) ||
                t.Raw.StartsWith("remote:", StringComparison.OrdinalIgnoreCase) ||
                t.Raw.StartsWith("r:", StringComparison.OrdinalIgnoreCase) ||
                t.Raw.StartsWith("tag:", StringComparison.OrdinalIgnoreCase) ||
                t.Raw.StartsWith("t:", StringComparison.OrdinalIgnoreCase));

            if (hasIncluded)
                return Models.FilterMode.Included;

            var hasExcluded = _histories.SearchTokens.Any(t =>
                t.Raw.StartsWith("-branch:", StringComparison.OrdinalIgnoreCase) ||
                t.Raw.StartsWith("-b:", StringComparison.OrdinalIgnoreCase) ||
                t.Raw.StartsWith("-remote:", StringComparison.OrdinalIgnoreCase) ||
                t.Raw.StartsWith("-r:", StringComparison.OrdinalIgnoreCase) ||
                t.Raw.StartsWith("-tag:", StringComparison.OrdinalIgnoreCase) ||
                t.Raw.StartsWith("-t:", StringComparison.OrdinalIgnoreCase));

            return hasExcluded ? Models.FilterMode.Excluded : Models.FilterMode.None;
        }

        private Models.FilterMode GetBranchNodeFilterMode(BranchTreeNode node)
        {
            if (_histories == null)
                return Models.FilterMode.None;

            var isLocal = node.Path.StartsWith("refs/heads/", StringComparison.Ordinal);
            var names = new List<string>();

            if (node.Backend is Models.Branch b)
                names.Add(isLocal ? b.Name : b.FriendlyName);
            else
                names.Add(isLocal ? node.Path.Substring("refs/heads/".Length) : node.Path.Substring("refs/remotes/".Length));

            bool includes = false;
            bool excludes = false;
            foreach (var token in _histories.SearchTokens)
            {
                var neg = token.Raw.StartsWith("-", StringComparison.Ordinal);
                var check = neg ? token.Raw[1..] : token.Raw;

                bool match = false;
                if (isLocal && (check.StartsWith("branch:", StringComparison.OrdinalIgnoreCase) || check.StartsWith("b:", StringComparison.OrdinalIgnoreCase)))
                {
                    var value = check.Substring(check.IndexOf(':') + 1).Trim();
                    match = names.Any(n => n.Contains(value, StringComparison.OrdinalIgnoreCase));
                }
                else if (!isLocal && (check.StartsWith("remote:", StringComparison.OrdinalIgnoreCase) || check.StartsWith("r:", StringComparison.OrdinalIgnoreCase)))
                {
                    var value = check.Substring(check.IndexOf(':') + 1).Trim();
                    match = names.Any(n => n.Contains(value, StringComparison.OrdinalIgnoreCase));
                }

                if (match)
                {
                    if (neg)
                        excludes = true;
                    else
                        includes = true;
                }
            }

            if (includes)
                return Models.FilterMode.Included;
            if (excludes)
                return Models.FilterMode.Excluded;
            return Models.FilterMode.None;
        }

        private Models.FilterMode GetTagFilterMode(string tagName)
        {
            if (_histories == null)
                return Models.FilterMode.None;

            bool includes = false;
            bool excludes = false;
            foreach (var token in _histories.SearchTokens)
            {
                var neg = token.Raw.StartsWith("-", StringComparison.Ordinal);
                var check = neg ? token.Raw[1..] : token.Raw;
                if (!check.StartsWith("tag:", StringComparison.OrdinalIgnoreCase) && !check.StartsWith("t:", StringComparison.OrdinalIgnoreCase))
                    continue;

                var value = check.Substring(check.IndexOf(':') + 1).Trim();
                if (tagName.Contains(value, StringComparison.OrdinalIgnoreCase))
                {
                    if (neg)
                        excludes = true;
                    else
                        includes = true;
                }
            }

            if (includes)
                return Models.FilterMode.Included;
            if (excludes)
                return Models.FilterMode.Excluded;
            return Models.FilterMode.None;
        }

        private void AddSearchTokenIfMissing(string token)
        {
            if (_histories == null || string.IsNullOrEmpty(token))
                return;

            if (_histories.SearchTokens.Any(t => t.Raw.Equals(token, StringComparison.OrdinalIgnoreCase)))
                return;

            _histories.AddFilter(token);
        }

        private void RemoveSearchTokenIgnoreCase(string token)
        {
            if (_histories == null || string.IsNullOrEmpty(token))
                return;

            for (int i = _histories.SearchTokens.Count - 1; i >= 0; i--)
            {
                if (_histories.SearchTokens[i].Raw.Equals(token, StringComparison.OrdinalIgnoreCase))
                    _histories.SearchTokens.RemoveAt(i);
            }
        }

        private void RemoveSearchTokensByPrefix(string prefix)
        {
            if (_histories == null || string.IsNullOrEmpty(prefix))
                return;

            for (int i = _histories.SearchTokens.Count - 1; i >= 0; i--)
            {
                var token = _histories.SearchTokens[i];
                var check = token.Raw.StartsWith("-", StringComparison.Ordinal) ? token.Raw[1..] : token.Raw;
                if (check.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    _histories.SearchTokens.RemoveAt(i);
            }
        }

        private void SyncSearchTokensFromPersistedHistoryFilters()
        {
            if (_histories == null || _uiStates.HistoryFilters.Count == 0)
                return;

            foreach (var filter in _uiStates.HistoryFilters.ToArray())
            {
                var token = BuildTokenFromHistoryFilter(filter);
                if (!string.IsNullOrEmpty(token))
                    AddSearchTokenIfMissing(token);
            }
        }

        private void RemoveSearchTokensForHistoryFilter(Models.HistoryFilter filter)
        {
            var token = BuildTokenFromHistoryFilter(filter);
            if (string.IsNullOrEmpty(token))
                return;

            RemoveSearchTokenIgnoreCase(token);
        }

        private string BuildTokenFromHistoryFilter(Models.HistoryFilter filter)
        {
            if (filter == null || filter.Mode == Models.FilterMode.None || string.IsNullOrEmpty(filter.Pattern))
                return null;

            var prefix = filter.Mode == Models.FilterMode.Excluded ? "-" : string.Empty;
            return filter.Type switch
            {
                Models.FilterType.Tag => $"{prefix}t:{filter.Pattern}",
                Models.FilterType.LocalBranch => $"{prefix}b:{TrimBranchRef(filter.Pattern, "refs/heads/")}",
                Models.FilterType.LocalBranchFolder => $"{prefix}b:{TrimBranchRef(filter.Pattern, "refs/heads/")}",
                Models.FilterType.RemoteBranch => $"{prefix}r:{TrimBranchRef(filter.Pattern, "refs/remotes/")}",
                Models.FilterType.RemoteBranchFolder => $"{prefix}r:{TrimBranchRef(filter.Pattern, "refs/remotes/")}",
                _ => null,
            };
        }

        private static string TrimBranchRef(string pattern, string prefix)
        {
            if (string.IsNullOrEmpty(pattern))
                return pattern;

            return pattern.StartsWith(prefix, StringComparison.Ordinal) ? pattern[prefix.Length..] : pattern;
        }

        private void ResetBranchTreeFilterMode(List<BranchTreeNode> nodes)
        {
            foreach (var node in nodes)
            {
                node.FilterMode = Models.FilterMode.None;
                if (!node.IsBranch)
                    ResetBranchTreeFilterMode(node.Children);
            }
        }

        private void ResetTagFilterMode()
        {
            if (VisibleTags is TagCollectionAsTree tree)
            {
                var filters = new Dictionary<string, Models.FilterMode>();
                foreach (var node in tree.Tree)
                    node.UpdateFilterMode(filters);
            }
            else if (VisibleTags is TagCollectionAsList list)
            {
                foreach (var item in list.TagItems)
                    item.FilterMode = Models.FilterMode.None;
            }
        }

        private void ValidateHistoryFilters(bool forBranch)
        {
            if (_historyFilterMode == Models.FilterMode.None)
                return;

            var set = new HashSet<string>();

            if (forBranch)
            {
                foreach (var b in _branches)
                    set.Add(b.FullName);

                foreach (var f in _uiStates.HistoryFilters)
                {
                    if (f.Type is Models.FilterType.LocalBranch or Models.FilterType.RemoteBranch)
                        f.IsValid = set.Contains(f.Pattern);
                }
            }
            else
            {
                foreach (var t in _tags)
                    set.Add(t.Name);

                foreach (var f in _uiStates.HistoryFilters)
                {
                    if (f.Type is Models.FilterType.Tag)
                        f.IsValid = set.Contains(f.Pattern);
                }
            }
        }

        private BranchTreeNode FindBranchNode(List<BranchTreeNode> nodes, string path)
        {
            if (string.IsNullOrEmpty(path))
                return null;

            foreach (var node in nodes)
            {
                if (node.Path.Equals(path, StringComparison.Ordinal))
                    return node;

                if (path.StartsWith(node.Path, StringComparison.Ordinal))
                {
                    var founded = FindBranchNode(node.Children, path);
                    if (founded != null)
                        return founded;
                }
            }

            return null;
        }

        private void AutoFetchByTimer(object sender)
        {
            try
            {
                Dispatcher.UIThread.Invoke(AutoFetchOnUIThread);
            }
            catch
            {
                // Ignore exception.
            }
        }

        private async Task AutoFetchOnUIThread()
        {
            if (IsAutoFetching)
                return;

            CommandLog log = null;

            try
            {
                if (!Preferences.Instance.EnableAutoFetch || !CanCreatePopup())
                {
                    _lastFetchTime = DateTime.Now;
                    return;
                }

                var lockFile = Path.Combine(GitDir, "index.lock");
                if (File.Exists(lockFile))
                    return;

                var now = DateTime.Now;
                var desire = _lastFetchTime.AddMinutes(Preferences.Instance.AutoFetchInterval);
                if (desire > now)
                    return;

                var remotes = new List<string>();
                foreach (var r in _remotes)
                {
                    if (!r.DisableAutoFetch)
                        remotes.Add(r.Name);
                }

                if (remotes.Count == 0)
                    return;

                IsAutoFetching = true;
                log = CreateLog("Auto-Fetch");

                foreach (var remote in remotes)
                    await new Commands.Fetch(FullPath, remote).Use(log).RunAsync();

                _lastFetchTime = DateTime.Now;
            }
            catch
            {
                // Ignore all exceptions.
            }

            IsAutoFetching = false;
            log?.Complete();
        }

        private readonly string _gitCommonDir = null;
        private Models.RepositorySettings _settings = null;
        private Models.RepositoryUIStates _uiStates = null;
        private Models.FilterMode _historyFilterMode = Models.FilterMode.None;
        private bool _hasAllowedSignersFile = false;
        private ulong _queryLocalChangesTimes = 0;

        private Models.Watcher _watcher = null;
        private Histories _histories = null;
        private WorkingCopy _workingCopy = null;
        private TerminalViewModel _terminal = null;
        private StashesPage _stashesPage = null;
        private int _selectedViewIndex = 0;

        private int _localBranchesCount = 0;
        private int _localChangesCount = 0;
        private int _stashesCount = 0;

        private bool _isSearchingCommits = false;
        private SearchCommitContext _searchCommitContext = null;

        private string _filter = string.Empty;
        private List<Models.Remote> _remotes = [];
        private List<Models.Branch> _branches = [];
        private Models.Branch _currentBranch = null;
        private List<BranchTreeNode> _localBranchTrees = [];
        private List<BranchTreeNode> _remoteBranchTrees = [];
        private List<Worktree> _worktrees = [];
        private List<Models.Tag> _tags = [];
        private object _visibleTags = null;
        private List<Models.Submodule> _submodules = [];
        private object _visibleSubmodules = null;
        private string _navigateToCommitDelayed = string.Empty;

        private bool _isAutoFetching = false;
        private Timer _autoFetchTimer = null;
        private DateTime _lastFetchTime = DateTime.MinValue;

        private Models.BisectState _bisectState = Models.BisectState.None;
        private bool _isBisectCommandRunning = false;

        private CancellationTokenSource _cancellationRefreshBranches = null;
        private CancellationTokenSource _cancellationRefreshTags = null;
        private CancellationTokenSource _cancellationRefreshWorkingCopyChanges = null;
        private CancellationTokenSource _cancellationRefreshCommits = null;
        private CancellationTokenSource _cancellationRefreshStashes = null;
    }
}
