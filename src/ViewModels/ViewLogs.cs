using System;
using Avalonia.Collections;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SourceGit.ViewModels
{
    public class ViewLogs : ObservableObject, IDisposable
    {
        public AvaloniaList<CommandLog> Logs
        {
            get => _repo.Logs;
        }

        public CommandLog SelectedLog
        {
            get => _selectedLog;
            set => SetProperty(ref _selectedLog, value);
        }

        public ViewLogs(Repository repo)
        {
            _repo = repo;
            _selectedLog = repo.Logs?.Count > 0 ? repo.Logs[0] : null;
            _repo.Logs.CollectionChanged += OnLogsCollectionChanged;
        }

        public void Dispose()
        {
            _repo.Logs.CollectionChanged -= OnLogsCollectionChanged;
        }

        public void ClearAll()
        {
            SelectedLog = null;
            Logs.Clear();
        }

        public void RemoveLog(CommandLog log)
        {
            if (SelectedLog == log)
                SelectedLog = null;
            Logs.Remove(log);
        }

        private void OnLogsCollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Add && e.NewItems.Count > 0)
            {
                SelectedLog = e.NewItems[^1] as CommandLog;
            }
        }

        private Repository _repo = null;
        private CommandLog _selectedLog = null;
    }
}
