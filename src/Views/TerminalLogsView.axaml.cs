using Avalonia.Controls;
using Avalonia.Interactivity;

namespace SourceGit.Views
{
    public partial class TerminalLogsView : UserControl
    {
        public TerminalLogsView()
        {
            InitializeComponent();
        }

        private void OnLogSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is ListBox list && list.SelectedItem != null)
                list.ScrollIntoView(list.SelectedItem);
        }

        private async void OnCopyLog(object sender, RoutedEventArgs e)
        {
            var log = (sender as MenuItem)?.DataContext as ViewModels.CommandLog;
            if (log == null)
                log = (sender as Button)?.DataContext as ViewModels.CommandLog;
            if (log == null && DataContext is ViewModels.ViewLogs vm)
                log = vm.SelectedLog;

            if (log != null)
                await this.CopyTextAsync(log.Content);

            e.Handled = true;
        }

        private void OnDeleteLog(object sender, RoutedEventArgs e)
        {
            var log = (sender as MenuItem)?.DataContext as ViewModels.CommandLog;
            if (log == null)
                log = (sender as Button)?.DataContext as ViewModels.CommandLog;

            if (log != null && DataContext is ViewModels.ViewLogs vm)
            {
                vm.RemoveLog(log);
            }
            e.Handled = true;
        }

        private void OnClearAll(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.ViewLogs vm)
            {
                vm.ClearAll();
            }
            e.Handled = true;
        }
    }
}
