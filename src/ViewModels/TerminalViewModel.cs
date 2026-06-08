using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SourceGit.ViewModels
{
    public class TerminalViewModel : ObservableObject, IDisposable
    {
        public ObservableCollection<TerminalGroup> Groups { get; } = new();

        public TerminalGroup SelectedGroup
        {
            get => _selectedGroup;
            set => SetProperty(ref _selectedGroup, value);
        }

        public bool IsSearchVisible
        {
            get => _isSearchVisible;
            set => SetProperty(ref _isSearchVisible, value);
        }

        public string SearchText
        {
            get => _searchText;
            set
            {
                if (SetProperty(ref _searchText, value))
                    OnSearchTextChanged();
            }
        }

        public int SearchResultCount
        {
            get => _searchResultCount;
            private set => SetProperty(ref _searchResultCount, value);
        }

        public bool UseSideBarLayout
        {
            get => _useSideBarLayout;
            set
            {
                if (SetProperty(ref _useSideBarLayout, value))
                {
                    OnPropertyChanged(nameof(LayoutColumns));
                }
            }
        }

        public string LayoutColumns
        {
            get => _useSideBarLayout ? "*,Auto,Auto" : "*";
        }

        public TerminalGroup RenamingGroup
        {
            get => _renamingGroup;
            set => SetProperty(ref _renamingGroup, value);
        }

        public string NewTitle
        {
            get => _newTitle;
            set => SetProperty(ref _newTitle, value);
        }

        public ViewLogs ViewLogs
        {
            get;
        }

        public List<Models.ShellOrTerminal> AvailableShells
        {
            get
            {
                var list = new List<Models.ShellOrTerminal>();
                var supported = Models.ShellOrTerminal.Supported;

                // Prioritize internal shells
                list.AddRange(supported.FindAll(x => x.IsInternal));
                list.AddRange(supported.FindAll(x => !x.IsInternal));

                return list;
            }
        }

        public Repository Repo { get; }

        public TerminalViewModel(Repository repo)
        {
            Repo = repo;
            if (repo != null)
            {
                _workingDirectory = repo.FullPath;
                ViewLogs = new ViewLogs(repo);
            }
        }

        public void NewSession(Models.ShellOrTerminal shell = null)
        {
            if (shell == null)
            {
                shell = AvailableShells.Find(x => x.IsInternal);
                if (shell == null)
                    return;
            }

            if (!shell.IsInternal)
            {
                // Launch external terminal
                var startInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = shell.Exec,
                    Arguments = shell.Args,
                    WorkingDirectory = _workingDirectory,
                    UseShellExecute = true,
                };

                try
                {
                    System.Diagnostics.Process.Start(startInfo);
                }
                catch (Exception ex)
                {
                    Models.Notification.Send(_workingDirectory, $"Failed to start external terminal: {ex.Message}", true);
                }
                return;
            }

            var instance = new TerminalInstance(_workingDirectory, shell);
            instance.OnExit = () => CloseSession(instance);
            var group = new TerminalGroup(instance);
            Groups.Add(group);
            SelectedGroup = group;
        }

        public void NewSessionWithCommand(string command, string title = "")
        {
            var shell = AvailableShells.Find(x => x.IsInternal);
            if (shell == null)
                return;

            var instance = new TerminalInstance(_workingDirectory, shell, command);
            if (!string.IsNullOrEmpty(title))
                instance.Title = title;
            instance.OnExit = () => CloseSession(instance);
            var group = new TerminalGroup(instance);
            Groups.Add(group);
            SelectedGroup = group;
        }

        public void CloseSession(TerminalInstance instance)
        {
            if (instance == null)
                return;

            TerminalGroup targetGroup = null;
            foreach (var group in Groups)
            {
                if (group.Panes.Contains(instance))
                {
                    targetGroup = group;
                    break;
                }
            }

            if (targetGroup == null)
                return;

            targetGroup.Panes.Remove(instance);
            instance.Dispose();

            if (targetGroup.Panes.Count == 0)
            {
                var idx = Groups.IndexOf(targetGroup);
                bool isSelected = SelectedGroup == targetGroup;

                Groups.Remove(targetGroup);
                targetGroup.Dispose();

                if (isSelected)
                {
                    if (Groups.Count > 0)
                    {
                        int nextIdx = Math.Max(0, idx - 1);
                        SelectedGroup = Groups[nextIdx];
                    }
                    else
                    {
                        SelectedGroup = null;
                    }
                }
            }
        }

        public void SplitSession(TerminalInstance instance)
        {
            if (instance == null)
                return;

            TerminalGroup targetGroup = null;
            foreach (var group in Groups)
            {
                if (group.Panes.Contains(instance))
                {
                    targetGroup = group;
                    break;
                }
            }

            if (targetGroup == null || targetGroup.IsSplit)
                return;

            var newInstance = new TerminalInstance(instance.WorkingDirectory, instance.Shell, instance.EnvironmentVariables);
            newInstance.OnExit = () => CloseSession(newInstance);
            targetGroup.Panes.Add(newInstance);
        }

        public void DuplicateSession(TerminalInstance instance)
        {
            if (instance == null)
                return;
            var newInstance = new TerminalInstance(instance.WorkingDirectory, instance.Shell);
            newInstance.OnExit = () => CloseSession(newInstance);
            newInstance.Title = instance.Title;
            var group = new TerminalGroup(newInstance);
            Groups.Add(group);
            SelectedGroup = group;
        }

        public void CloseGroup(TerminalGroup group)
        {
            if (group == null)
                return;
            var panes = group.Panes.ToList();
            foreach (var pane in panes)
            {
                CloseSession(pane);
            }
        }

        public void StartRename(TerminalGroup group)
        {
            if (group == null)
                return;

            if (_renamingGroup != null)
                _renamingGroup.IsRenaming = false;

            NewTitle = group.Title;
            RenamingGroup = group;
            group.IsRenaming = true;
        }

        public void ConfirmRename()
        {
            if (_renamingGroup != null)
            {
                if (!string.IsNullOrWhiteSpace(_newTitle))
                    _renamingGroup.Title = _newTitle;
                _renamingGroup.IsRenaming = false;
            }
            RenamingGroup = null;
        }

        public void CancelRename()
        {
            if (_renamingGroup != null)
                _renamingGroup.IsRenaming = false;
            RenamingGroup = null;
        }

        public void GotoPrevSession()
        {
            if (Groups.Count <= 1)
                return;
            var idx = Groups.IndexOf(_selectedGroup);
            if (idx > 0)
                SelectedGroup = Groups[idx - 1];
            else
                SelectedGroup = Groups[Groups.Count - 1];
        }

        public void GotoNextSession()
        {
            if (Groups.Count <= 1)
                return;
            var idx = Groups.IndexOf(_selectedGroup);
            if (idx < Groups.Count - 1)
                SelectedGroup = Groups[idx + 1];
            else
                SelectedGroup = Groups[0];
        }

        public void SelectSessionByIndex(int index)
        {
            if (index >= 0 && index < Groups.Count)
            {
                SelectedGroup = Groups[index];
            }
        }

        public void ClearCurrentSession()
        {
            SelectedGroup?.Panes?.FirstOrDefault()?.Model?.Feed("\u001b[2J\u001b[H"); // ANSI clear screen
        }

        public void ClearSession(TerminalInstance instance)
        {
            instance?.Model?.Feed("\u001b[2J\u001b[H");
        }

        public void OpenTerminal()
        {
            NewSession();
        }

        public void SearchNext()
        {
            SelectedGroup?.Panes?.FirstOrDefault()?.Model?.SelectNextSearchResult();
        }

        public void SearchPrev()
        {
            SelectedGroup?.Panes?.FirstOrDefault()?.Model?.SelectPreviousSearchResult();
        }

        public void CloseSearch()
        {
            IsSearchVisible = false;
            SearchText = string.Empty;
            SelectedGroup?.Panes?.FirstOrDefault()?.Model?.ClearSelection();
        }

        private void OnSearchTextChanged()
        {
            var active = SelectedGroup?.Panes?.FirstOrDefault();
            if (active != null)
            {
                SearchResultCount = active.Model.Search(_searchText);
            }
        }

        public void Dispose()
        {
            foreach (var group in Groups)
                group.Dispose();
            Groups.Clear();
            _selectedGroup = null;

            ViewLogs?.Dispose();
        }

        private string _workingDirectory;
        private TerminalGroup _selectedGroup;
        private bool _isSearchVisible;
        private string _searchText = string.Empty;
        private int _searchResultCount;
        private bool _useSideBarLayout = false;
        private TerminalGroup _renamingGroup = null;
        private string _newTitle = string.Empty;
    }
}

