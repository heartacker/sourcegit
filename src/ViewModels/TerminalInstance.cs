using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Porta.Pty;
using SvcSystems.UI.Terminal;

namespace SourceGit.ViewModels
{
    public class TerminalInstance : ObservableObject, IDisposable
    {
        public TerminalControlModel Model { get; }
        public Models.ShellOrTerminal Shell { get; }
        public string WorkingDirectory { get; }
        public Action OnExit { get; set; }

        public string Title
        {
            get => _title;
            set => SetProperty(ref _title, value);
        }

        public TerminalInstance(string workingDirectory, Models.ShellOrTerminal shell)
        {
            _title = Path.GetFileName(workingDirectory);
            Shell = shell;
            WorkingDirectory = workingDirectory;
            _workingDirectory = workingDirectory;
            _shellConfig = shell;
            Model = new TerminalControlModel(new TerminalOptions
            {
                Cols = 80,
                Rows = 24,
                Scrollback = 1000,
                ReflowOnResize = false,
                // Unix PTY may not set ONLCR, so LF needs manual CR conversion.
                // Windows ConPTY already handles CRLF, so skip to avoid double-CR.
                ConvertEol = !RuntimeInformation.IsOSPlatform(OSPlatform.Windows),
            });

            _cts = new CancellationTokenSource();

            // Wait for TerminalControl to lay out and report real size,
            // then start PTY with correct cols/rows.
            Model.SizeChanged += OnInitialSizeChanged;
        }

        private void OnInitialSizeChanged(object sender, TerminalSizeChangedEventArgs e)
        {
            Model.SizeChanged -= OnInitialSizeChanged;
            _initialCols = e.Cols;
            _initialRows = e.Rows;
            _ = StartPtyAsync(WorkingDirectory, _shellConfig, _cts.Token);
        }

        private async Task StartPtyAsync(string workingDirectory, Models.ShellOrTerminal shellConfig, CancellationToken token)
        {
            var app = shellConfig.Exec;
            var args = shellConfig.Args ?? string.Empty;

            if (string.IsNullOrEmpty(app))
            {
                app = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "powershell.exe" : "bash";
                args = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "-NoLogo" : "-i";
            }

            var env = new Dictionary<string, string>();
            var current = Environment.GetEnvironmentVariables();
            foreach (System.Collections.DictionaryEntry entry in current)
            {
                var key = entry.Key.ToString();
                // Strip all VSCode/IDE shell integration artifacts
                if (key.StartsWith("VSCODE_", StringComparison.OrdinalIgnoreCase) ||
                    key.Equals("ELECTRON_RUN_AS_NODE", StringComparison.OrdinalIgnoreCase) ||
                    key.Equals("TERM_PROGRAM", StringComparison.OrdinalIgnoreCase) ||
                    key.Equals("TERM_PROGRAM_VERSION", StringComparison.OrdinalIgnoreCase) ||
                    key.Equals("PROMPT_COMMAND", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                env[key] = entry.Value.ToString();
            }

            env["PROMPT_COMMAND"] = string.Empty;
            env["TERM"] = "xterm-256color";
            env["COLORTERM"] = "truecolor";
            env["TERM_PROGRAM"] = "SourceGit";
            env["TERM_PROGRAM_VERSION"] = "1.0";
            env["COLUMNS"] = _initialCols.ToString();
            env["LINES"] = _initialRows.ToString();
            env["SOURCEGIT_TERMINAL"] = "1";

            // Inject GIT_EDITOR to use SourceGit's built-in GUI editor
            var processPath = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(processPath))
            {
                // Note: We use --core-editor which opens CommitMessageEditor in standalone mode.
                // We wrap path in quotes if it contains spaces.
                var editorPath = processPath.Contains(' ') ? $"\"{processPath}\"" : processPath;
                env["GIT_EDITOR"] = $"{editorPath} --core-editor";
            }

            if (!env.ContainsKey("LANG"))
                env["LANG"] = "en_US.UTF-8";
            env["LC_ALL"] = "en_US.UTF-8";
            env["LANGUAGE"] = "en_US.UTF-8";

            var options = new PtyOptions
            {
                App = app,
                CommandLine = string.IsNullOrEmpty(args) ? [] : [args],
                Cwd = workingDirectory,
                Cols = _initialCols,
                Rows = _initialRows,
                Environment = env,
            };

            try
            {
                _connection = await PtyProvider.SpawnAsync(options, token);
                if (_connection != null)
                {
                    // Apply any resize that happened while spawning
                    if (_pendingCols > 0 && _pendingRows > 0)
                        _connection.Resize(_pendingCols, _pendingRows);

                    _connection.ProcessExited += (s, e) =>
                    {
                        if (Interlocked.CompareExchange(ref _disposed, 0, 0) == 0)
                        {
                            Avalonia.Threading.Dispatcher.UIThread.Post(() => OnExit?.Invoke());
                        }
                    };

                    Model.UserInput += OnTerminalUserInput;
                    Model.SizeChanged += OnTerminalSizeChanged;

                    // Start reading loop
                    _readTask = Task.Run(async () =>
                    {
                        var buffer = new byte[16384];
                        try
                        {
                            while (!token.IsCancellationRequested)
                            {
                                int read = await _connection.ReaderStream.ReadAsync(buffer, 0, buffer.Length, token);
                                if (read <= 0)
                                    break;

                                lock (_outputLock)
                                {
                                    _outputBuffer.Write(buffer, 0, read);
                                    if (!_isFlushPending)
                                    {
                                        _isFlushPending = true;
                                        Avalonia.Threading.Dispatcher.UIThread.Post(FlushOutputBuffer, Avalonia.Threading.DispatcherPriority.Background);
                                    }
                                }
                            }
                        }
                        catch (OperationCanceledException) { }
                        catch (Exception ex)
                        {
                            if (Interlocked.CompareExchange(ref _disposed, 0, 0) == 0)
                            {
                                Avalonia.Threading.Dispatcher.UIThread.Post(() => Model.Feed($"\r\n[Error reading from PTY: {ex.Message}]\r\n"));
                            }
                        }
                    }, token);
                }
            }
            catch (Exception ex)
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() => Model.Feed($"Failed to spawn PTY: {ex.Message}\r\n"));
            }
        }

        private void FlushOutputBuffer()
        {
            byte[] data;
            lock (_outputLock)
            {
                data = _outputBuffer.ToArray();
                _outputBuffer.SetLength(0);
                _isFlushPending = false;
            }

            if (data.Length > 0 && Interlocked.CompareExchange(ref _disposed, 0, 0) == 0)
            {
                Model.Feed(data, data.Length);
            }
        }

        public void Paste(string text)
        {
            if (_connection != null && !string.IsNullOrEmpty(text))
            {
                try
                {
                    // Normalize newlines for PTY: typically PTY expects \r for Enter
                    var normalized = text.Replace("\r\n", "\r").Replace("\n", "\r");
                    var data = System.Text.Encoding.UTF8.GetBytes(normalized);
                    _connection.WriterStream.Write(data);
                    _connection.WriterStream.Flush();
                }
                catch { }
            }
        }

        private void OnTerminalUserInput(object sender, TerminalUserInputEventArgs e)
        {
            if (_connection != null)
            {
                try
                {
                    _connection.WriterStream.Write(e.Data.Span);
                    _connection.WriterStream.Flush();
                }
                catch { }
            }
        }

        private void OnTerminalSizeChanged(object sender, TerminalSizeChangedEventArgs e)
        {
            _pendingCols = e.Cols;
            _pendingRows = e.Rows;
            _connection?.Resize(e.Cols, e.Rows);
        }

        public void Resize(int cols, int rows)
        {
            _pendingCols = cols;
            _pendingRows = rows;
            _connection?.Resize(cols, rows);
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 1)
                return;

            _cts.Cancel();
            if (_connection != null)
            {
                Model.UserInput -= OnTerminalUserInput;
                Model.SizeChanged -= OnTerminalSizeChanged;
                _connection.Dispose();
                _connection = null;
            }

            lock (_outputLock)
            {
                _outputBuffer.Dispose();
            }

            _cts.Dispose();
        }

        private IPtyConnection _connection;
        private readonly CancellationTokenSource _cts;
        private Task _readTask;
        private int _disposed = 0;
        private int _pendingCols = 0;
        private int _pendingRows = 0;
        private string _workingDirectory;
        private Models.ShellOrTerminal _shellConfig;
        private int _initialCols = 80;
        private int _initialRows = 24;
        private readonly object _outputLock = new object();
        private readonly MemoryStream _outputBuffer = new MemoryStream();
        private bool _isFlushPending = false;
        private string _title;
    }
}
