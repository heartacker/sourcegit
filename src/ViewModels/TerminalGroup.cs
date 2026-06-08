using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SourceGit.ViewModels
{
    public class TerminalGroup : ObservableObject, IDisposable
    {
        public ObservableCollection<TerminalInstance> Panes { get; } = new();

        public TerminalInstance FirstPane => Panes.Count > 0 ? Panes[0] : null;
        public TerminalInstance SecondPane => Panes.Count > 1 ? Panes[1] : null;

        public bool IsSplit => Panes.Count > 1;

        public string TabTitle
        {
            get
            {
                if (IsRenaming && !string.IsNullOrWhiteSpace(_title))
                    return _title;
                return string.Join(" ｜ ", Panes.Select(x => x.Title));
            }
        }

        public string Title
        {
            get => _title;
            set
            {
                if (SetProperty(ref _title, value))
                {
                    OnPropertyChanged(nameof(TabTitle));
                }
            }
        }

        public bool IsRenaming
        {
            get => _isRenaming;
            set
            {
                if (SetProperty(ref _isRenaming, value))
                {
                    OnPropertyChanged(nameof(TabTitle));
                }
            }
        }

        public TerminalGroup(TerminalInstance firstPane)
        {
            Panes.CollectionChanged += OnPanesCollectionChanged;
            if (firstPane != null)
            {
                _title = firstPane.Title;
                Panes.Add(firstPane);
            }
        }

        private void OnPanesCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            OnPropertyChanged(nameof(IsSplit));
            OnPropertyChanged(nameof(TabTitle));
            OnPropertyChanged(nameof(FirstPane));
            OnPropertyChanged(nameof(SecondPane));

            if (e.NewItems != null)
            {
                foreach (TerminalInstance pane in e.NewItems)
                {
                    pane.PropertyChanged += OnPanePropertyChanged;
                }
            }

            if (e.OldItems != null)
            {
                foreach (TerminalInstance pane in e.OldItems)
                {
                    pane.PropertyChanged -= OnPanePropertyChanged;
                }
            }
        }

        private void OnPanePropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(TerminalInstance.Title))
            {
                OnPropertyChanged(nameof(TabTitle));
            }
        }

        public void Dispose()
        {
            Panes.CollectionChanged -= OnPanesCollectionChanged;
            foreach (var pane in Panes)
            {
                pane.PropertyChanged -= OnPanePropertyChanged;
                pane.Dispose();
            }
            Panes.Clear();
        }

        private string _title;
        private bool _isRenaming;
    }
}
