using System;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SvcSystems.UI.Terminal;

namespace SourceGit.Views
{
    public partial class TerminalManagerView : UserControl
    {
        private static readonly Regex UrlRegex = new Regex(@"https?://[^\s""'<>]+", RegexOptions.Compiled | RegexOptions.IgnoreCase);

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

        private void OnOpenSearch(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.TerminalViewModel vm)
            {
                vm.IsSearchVisible = !vm.IsSearchVisible;
                if (vm.IsSearchVisible)
                {
                    var searchBox = this.FindControl<TextBox>("PART_SearchBox");
                    searchBox?.Focus();
                    searchBox?.SelectAll();
                }
            }
            e.Handled = true;
        }

        private void OnTerminalPointerPressed(object sender, PointerPressedEventArgs e)
        {
            var terminal = sender as TerminalControl;
            if (terminal == null || terminal.Model == null) return;

            // Feature 3: Smart Mouse Mode
            // If the terminal application (like vim/htop) is capturing the mouse, 
            // don't perform custom overrides.
            if (terminal.IsMouseModeActive) return;

            // Handle Ctrl + LeftClick for Links
            if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.GetCurrentPoint(terminal).Properties.IsLeftButtonPressed)
            {
                // Access internal _consoleTextSize and _surface to calculate cell
                var type = typeof(TerminalControl);
                var sizeField = type.GetField("_consoleTextSize", BindingFlags.NonPublic | BindingFlags.Instance);
                var surfaceField = type.GetField("_surface", BindingFlags.NonPublic | BindingFlags.Instance);

                if (sizeField != null && surfaceField != null)
                {
                    var cellSize = (Size)sizeField.GetValue(terminal);
                    var surface = (Control)surfaceField.GetValue(terminal);

                    if (cellSize.Width > 0 && cellSize.Height > 0 && surface != null)
                    {
                        var pos = e.GetPosition(surface);
                        var col = (int)Math.Floor(pos.X / cellSize.Width);
                        var row = (int)Math.Floor(pos.Y / cellSize.Height);

                        // Clamp to valid range
                        col = Math.Clamp(col, 0, terminal.Model.Terminal.Cols - 1);
                        row = Math.Clamp(row, 0, terminal.Model.Terminal.Rows - 1);

                        // Select the word at this position
                        terminal.Model.SelectWordOrExpression(row, col);
                        var word = terminal.Model.SelectedText;
                        
                        // Check if it's a URL
                        var match = UrlRegex.Match(word);
                        if (match.Success)
                        {
                            Native.OS.OpenBrowser(match.Value);
                            e.Handled = true;
                        }

                        // Clear selection to not disturb user
                        terminal.Model.ClearSelection();
                    }
                }
            }
        }

        private void OnTerminalLoaded(object sender, RoutedEventArgs e)
        {
            if (sender is TerminalControl terminal)
                OnTerminalCreated(terminal);
        }

        private void OnTerminalCreated(TerminalControl terminal)
        {
            // Feature 5: Paste Interception
            // Normalize clipboard content before it reaches the terminal buffer
            // Since ClipboardTextReaderOverride is internal, we use reflection to set it.
            var prop = typeof(TerminalControl).GetProperty("ClipboardTextReaderOverride", BindingFlags.NonPublic | BindingFlags.Instance);
            if (prop != null)
            {
                Func<Task<string>> reader = async () =>
                {
                    var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
                    if (clipboard != null)
                    {
                        var text = await clipboard.TryGetTextAsync();
                        if (!string.IsNullOrEmpty(text))
                        {
                            // Normalize newlines for PTY
                            return text.Replace("\r\n", "\r").Replace("\n", "\r");
                        }
                    }
                    return string.Empty;
                };
                prop.SetValue(terminal, reader);
            }
        }

        private void OnTerminalDragOver(object sender, DragEventArgs e)
        {
            if (e.Data.Contains(DataFormats.Files) || e.Data.Contains(DataFormats.Text))
            {
                e.DragEffects = DragDropEffects.Copy;
            }
            else
            {
                e.DragEffects = DragDropEffects.None;
            }
            e.Handled = true;
        }

        private async void OnTerminalDrop(object sender, DragEventArgs e)
        {
            if (DataContext is ViewModels.TerminalViewModel { SelectedInstance: { } instance })
            {
                if (e.Data.Contains(DataFormats.Files))
                {
                    var files = e.Data.GetFiles();
                    if (files != null)
                    {
                        var builder = new StringBuilder();
                        foreach (var file in files)
                        {
                            if (builder.Length > 0) builder.Append(' ');

                            var path = file.Path.LocalPath;
                            if (path.Contains(' '))
                            {
                                builder.Append('"');
                                builder.Append(path);
                                builder.Append('"');
                            }
                            else
                            {
                                builder.Append(path);
                            }
                        }

                        if (builder.Length > 0)
                        {
                            instance.Paste(builder.ToString());
                        }
                    }
                }
                else if (e.Data.Contains(DataFormats.Text))
                {
                    var text = await e.Data.GetTextAsync();
                    if (!string.IsNullOrEmpty(text))
                    {
                        instance.Paste(text);
                    }
                }
            }
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
