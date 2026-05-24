using System;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SvcSystems.UI.Terminal;

namespace SourceGit.Views
{
    public partial class TerminalManagerView : UserControl
    {
        public TerminalManagerView()
        {
            InitializeComponent();
        }

        protected override void OnDataContextChanged(EventArgs e)
        {
            base.OnDataContextChanged(e);

            if (_oldVm != null)
                _oldVm.PropertyChanged -= OnViewModelPropertyChanged;

            if (DataContext is ViewModels.TerminalViewModel vm)
            {
                vm.PropertyChanged += OnViewModelPropertyChanged;
                _oldVm = vm;

                // Trigger initial focus check
                Dispatcher.UIThread.Post(FocusProperControl, DispatcherPriority.Background);
            }
        }

        private void OnViewModelPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ViewModels.TerminalViewModel.SelectedInstance) ||
                e.PropertyName == "Instances")
            {
                // Use a lower priority for empty state to avoid catching Enter key from exit command
                var priority = (DataContext is ViewModels.TerminalViewModel vm && vm.Instances.Count == 0)
                    ? DispatcherPriority.ApplicationIdle
                    : DispatcherPriority.Background;

                Dispatcher.UIThread.Post(FocusProperControl, priority);
            }
        }

        private async void FocusProperControl()
        {
            if (DataContext is not ViewModels.TerminalViewModel vm)
                return;

            if (vm.Instances.Count == 0)
            {
                // Small delay to ensure any pending keyboard events (like Enter for 'exit') are processed
                await Task.Delay(100);

                // Focus the empty state SplitButton
                var emptyBtn = this.FindControl<SplitButton>("PART_EmptyNewBtn");
                if (emptyBtn != null)
                {
                    emptyBtn.Focus();
                }
                else
                {
                    // Fallback search if Name binding fails in Template
                    var btn = this.GetVisualDescendants().OfType<SplitButton>().FirstOrDefault(x => x.IsVisible);
                    btn?.Focus();
                }
            }
            else if (vm.SelectedInstance != null)
            {
                // Focus the TerminalControl
                var terminal = this.GetVisualDescendants().OfType<TerminalControl>().FirstOrDefault(x => x.IsVisible);
                terminal?.Focus();
            }
        }

        private void OnNewTerminal(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.TerminalViewModel vm)
                vm.NewSession();
            e.Handled = true;
        }

        private async void OnCopy(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.TerminalViewModel { SelectedInstance: { } instance })
            {
                var selected = instance.Model.SelectedText;
                if (!string.IsNullOrEmpty(selected))
                {
                    var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
                    if (clipboard != null)
                        await clipboard.SetTextAsync(selected);
                }
            }
            e.Handled = true;
        }

        private async void OnPaste(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.TerminalViewModel { SelectedInstance: { } instance })
            {
                var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
                if (clipboard != null)
                {
                    var text = await clipboard.TryGetTextAsync();
                    if (!string.IsNullOrEmpty(text))
                        instance.Paste(text);
                }
            }
            e.Handled = true;
        }

        private ViewModels.TerminalViewModel _oldVm;
    }
}
