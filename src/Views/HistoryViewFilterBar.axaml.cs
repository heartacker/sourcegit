using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace SourceGit.Views
{
    public partial class HistoryViewFilterBar : UserControl
    {
        public HistoryViewFilterBar()
        {
            InitializeComponent();
        }

        private void OnPointerPressed(object sender, PointerPressedEventArgs e)
        {
            _isDragging = true;
            _lastPoint = e.GetPosition(this);
            e.Pointer.Capture(sender as Control);
            e.Handled = true;
        }

        private void OnPointerMoved(object sender, PointerEventArgs e)
        {
            if (_isDragging)
            {
                var transform = Container.RenderTransform as TranslateTransform;
                if (transform != null)
                {
                    var currentPoint = e.GetPosition(this);
                    var delta = currentPoint - _lastPoint;
                    transform.X += delta.X;
                    transform.Y += delta.Y;
                }
                e.Handled = true;
            }
        }

        private void OnPointerReleased(object sender, PointerReleasedEventArgs e)
        {
            _isDragging = false;
            e.Pointer.Capture(null);
            e.Handled = true;
        }

        private void OnRemoveViewFilter(object sender, RoutedEventArgs e)
        {
            if (sender is Button { DataContext: Models.IHistoryViewFilter filter })
            {
                if (filter is Models.SoloFilter)
                {
                    var repoView = this.FindAncestorOfType<Repository>();
                    if (repoView is { DataContext: ViewModels.Repository repo })
                        repo.ClearSoloMode();
                }
                else if (filter is Models.FoldingFilter)
                {
                    ViewModels.Preferences.Instance.EnableLinearCommitFolding = false;
                }
            }

            e.Handled = true;
        }

        private bool _isDragging = false;
        private Point _lastPoint;
    }
}
