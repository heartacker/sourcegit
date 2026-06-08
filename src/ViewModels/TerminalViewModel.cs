using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SourceGit.ViewModels
{
    public class TerminalViewModel : ObservableObject, IDisposable
    {
        public ObservableCollection<TerminalInstance> Instances { get; } = new();

        public TerminalInstance SelectedInstance
        {
            get => _selectedInstance;
            set => SetProperty(ref _selectedInstance, value);
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
            set => SetProperty(ref _useSideBarLayout, value);
        }

        public TerminalInstance RenamingInstance
        {
            get => _renamingInstance;
            set => SetProperty(ref _renamingInstance, value);
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

        public TerminalViewModel(Repository repo)
        {
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
            Instances.Add(instance);
            SelectedInstance = instance;
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
            Instances.Add(instance);
            SelectedInstance = instance;
        }

        public void CloseSession(TerminalInstance instance)
        {
            if (instance == null)
                return;

            var idx = Instances.IndexOf(instance);
            bool isSelected = SelectedInstance == instance;

            Instances.Remove(instance);
            instance.Dispose();

            if (isSelected)
            {
                if (Instances.Count > 0)
                {
                    // Focus previous session if possible, otherwise first
                    int nextIdx = Math.Max(0, idx - 1);
                    SelectedInstance = Instances[nextIdx];
                }
                else
                {
                    SelectedInstance = null;
                }
            }
        }

        public void DuplicateSession(TerminalInstance instance)
        {
            if (instance == null)
                return;
            var newInstance = new TerminalInstance(instance.WorkingDirectory, instance.Shell);
            newInstance.OnExit = () => CloseSession(newInstance);
            newInstance.Title = instance.Title;
            Instances.Add(newInstance);
            SelectedInstance = newInstance;
        }

        public void StartRename(TerminalInstance instance)
        {
            if (instance == null)
                return;
            NewTitle = instance.Title;
            RenamingInstance = instance;
        }

        public void ConfirmRename()
        {
            if (_renamingInstance != null && !string.IsNullOrWhiteSpace(_newTitle))
            {
                _renamingInstance.Title = _newTitle;
            }
            RenamingInstance = null;
        }

        public void CancelRename()
        {
            RenamingInstance = null;
        }

        public void GotoPrevSession()
        {
            if (Instances.Count <= 1)
                return;
            var idx = Instances.IndexOf(_selectedInstance);
            if (idx > 0)
                SelectedInstance = Instances[idx - 1];
            else
                SelectedInstance = Instances[Instances.Count - 1];
        }

        public void GotoNextSession()
        {
            if (Instances.Count <= 1)
                return;
            var idx = Instances.IndexOf(_selectedInstance);
            if (idx < Instances.Count - 1)
                SelectedInstance = Instances[idx + 1];
            else
                SelectedInstance = Instances[0];
        }

        public void SelectSessionByIndex(int index)
        {
            if (index >= 0 && index < Instances.Count)
            {
                SelectedInstance = Instances[index];
            }
        }

        public void ClearCurrentSession()
        {
            SelectedInstance?.Model?.Feed("\u001b[2J\u001b[H"); // ANSI clear screen
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
            SelectedInstance?.Model?.SelectNextSearchResult();
        }

        public void SearchPrev()
        {
            SelectedInstance?.Model?.SelectPreviousSearchResult();
        }

        public void CloseSearch()
        {
            IsSearchVisible = false;
            SearchText = string.Empty;
            SelectedInstance?.Model?.ClearSelection();
        }

        private void OnSearchTextChanged()
        {
            if (SelectedInstance != null)
            {
                SearchResultCount = SelectedInstance.Model.Search(_searchText);
            }
        }

        public void Dispose()
        {
            foreach (var instance in Instances)
                instance.Dispose();
            Instances.Clear();
            _selectedInstance = null;

            ViewLogs?.Dispose();
        }

        private string _workingDirectory;
        private TerminalInstance _selectedInstance;
        private bool _isSearchVisible;
        private string _searchText = string.Empty;
        private int _searchResultCount;
        private bool _useSideBarLayout = false;
        private TerminalInstance _renamingInstance = null;
        private string _newTitle = string.Empty;
    }
}

