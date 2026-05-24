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

        public TerminalViewModel(string workingDirectory)
        {
            _workingDirectory = workingDirectory;
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
        }

        private string _workingDirectory;
        private TerminalInstance _selectedInstance;
        private bool _isSearchVisible;
        private string _searchText = string.Empty;
        private int _searchResultCount;
    }
}
