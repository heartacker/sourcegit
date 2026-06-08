using System;
using System.Text;

using Avalonia;
using Avalonia.Input;

namespace SourceGit.Views
{
    public partial class HistoriesCommitList
    {
        private Point? _pressedPosition;
        private PointerPressedEventArgs _pressedEvent;

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);

            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                _pressedPosition = e.GetPosition(this);
                _pressedEvent = e;
            }
        }

        protected override async void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);

            if (_pressedPosition.HasValue && _pressedEvent != null && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                var delta = e.GetPosition(this) - _pressedPosition.Value;
                if (Math.Abs(delta.X) > 8 || Math.Abs(delta.Y) > 8)
                {
                    var eventArgs = _pressedEvent;
                    _pressedPosition = null;
                    _pressedEvent = null;

                    var selected = SelectedItems;
                    if (selected.Count > 0)
                    {
                        var builder = new StringBuilder();
                        foreach (var item in selected)
                        {
                            if (item is Models.Commit commit)
                            {
                                if (builder.Length > 0)
                                    builder.Append(' ');
                                builder.Append(commit.SHA);
                            }
                        }

                        if (builder.Length > 0)
                        {
                            var data = new DataTransfer();
                            data.Add(DataTransferItem.Create(DataFormat.Text, builder.ToString()));
                            await DragDrop.DoDragDropAsync(eventArgs, data, DragDropEffects.Copy);
                        }
                    }
                }
            }
        }
    }
}
