using Avalonia.Controls;
using Avalonia.Input;

namespace SourceGit.Views
{
    public partial class BranchCompare : Window
    {
        public BranchCompare()
        {
            InitializeComponent();
        }

        private void MaximizeOrRestoreWindow(object sender, TappedEventArgs e)
        {
            if (WindowState == WindowState.Maximized)
                WindowState = WindowState.Normal;
            else
                WindowState = WindowState.Maximized;

            e.Handled = true;
        }

        private void CustomResizeWindow(object sender, PointerPressedEventArgs e)
        {
            if (sender is Border border)
            {
                if (border.Tag is WindowEdge edge)
                {
                    BeginResizeDrag(edge, e);
                }
            }
        }

        private void BeginMoveWindow(object sender, PointerPressedEventArgs e)
        {
            BeginMoveDrag(e);
        }

        private void OnChangeContextRequested(object sender, ContextRequestedEventArgs e)
        {
            if (DataContext is ViewModels.BranchCompare vm && sender is ChangeCollectionView view)
            {
                var menu = vm.CreateChangeContextMenu();
                view.OpenContextMenu(menu);
            }

            e.Handled = true;
        }

        private void OnPressedSHA(object sender, PointerPressedEventArgs e)
        {
            if (DataContext is ViewModels.BranchCompare vm && sender is TextBlock block)
                vm.NavigateTo(block.Text);

            e.Handled = true;
        }
    }
}
