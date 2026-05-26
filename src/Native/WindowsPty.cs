using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;

#if WINDOWS
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Console;
using Windows.Win32.System.Threading;
#endif

namespace SourceGit.Native
{
    // Define a local interface and args to break dependency on Porta.Pty assembly in Windows path
    internal class NativePtyExitedEventArgs : EventArgs
    {
        public int ExitCode { get; }
        public NativePtyExitedEventArgs(int exitCode) => ExitCode = exitCode;
    }

    internal interface INativePtyConnection : IDisposable
    {
        event EventHandler<NativePtyExitedEventArgs> ProcessExited;
        Stream ReaderStream { get; }
        Stream WriterStream { get; }
        void Resize(int cols, int rows);
        void Kill();
    }

    internal class NativePtyOptions
    {
        public string App { get; set; }
        public string[] CommandLine { get; set; }
        public string Cwd { get; set; }
        public int Cols { get; set; }
        public int Rows { get; set; }
        public IDictionary<string, string> Environment { get; set; }
    }

#if WINDOWS
#pragma warning disable CA1416
    [SupportedOSPlatform("windows")]
    internal sealed class WindowsPty : INativePtyConnection
    {
        private readonly ClosePseudoConsoleSafeHandle _hPC;
        private readonly SafeProcessHandle _hProcess;
        private readonly SafeThreadHandle _hThread;
        private readonly int _pid;
        private bool _isDisposed;

        public event EventHandler<NativePtyExitedEventArgs> ProcessExited;
        public Stream ReaderStream { get; }
        public Stream WriterStream { get; }
        public int Pid => _pid;
        public int ExitCode
        {
            get
            {
                if (PInvoke.GetExitCodeProcess(_hProcess, out var exitCode))
                    return (int)exitCode;
                return -1;
            }
        }

        private WindowsPty(ClosePseudoConsoleSafeHandle hPC, SafeProcessHandle hProcess, SafeThreadHandle hThread, int pid, Stream reader, Stream writer)
        {
            _hPC = hPC;
            _hProcess = hProcess;
            _hThread = hThread;
            _pid = pid;
            ReaderStream = reader;
            WriterStream = writer;

            // Start a thread to wait for process exit
            Task.Run(() =>
            {
                PInvoke.WaitForSingleObject(_hProcess, 0xFFFFFFFF);
                ProcessExited?.Invoke(this, new NativePtyExitedEventArgs(ExitCode));
            });
        }

        public static Task<INativePtyConnection> SpawnAsync(NativePtyOptions options, CancellationToken cancellationToken)
        {
            return Task.Run(() => SpawnInternal(options), cancellationToken);
        }

        private static INativePtyConnection SpawnInternal(NativePtyOptions options)
        {
            // 1. Create pipes
            if (!PInvoke.CreatePipe(out var hReadPipeOurSide, out var hWritePipePtySide, null, 0))
                throw new Exception("Failed to create input pipe");
            if (!PInvoke.CreatePipe(out var hReadPipePtySide, out var hWritePipeOurSide, null, 0))
                throw new Exception("Failed to create output pipe");

            // Ensure pipes are not inherited by child process (except ConPTY)
            PInvoke.SetHandleInformation(hReadPipeOurSide, 0x00000001 /* HANDLE_FLAG_INHERIT */, 0);
            PInvoke.SetHandleInformation(hWritePipeOurSide, 0x00000001 /* HANDLE_FLAG_INHERIT */, 0);

            // 2. Create Pseudo Console
            var size = new COORD { X = (short)options.Cols, Y = (short)options.Rows };
            var hr = PInvoke.CreatePseudoConsole(size, hReadPipePtySide, hWritePipePtySide, 0, out var hPC);
            if (hr.Failed)
                throw new Exception($"Failed to create Pseudo Console: {hr}");

            // Close PTY side handles as they are now owned by ConPTY
            hReadPipePtySide.Dispose();
            hWritePipePtySide.Dispose();

            // 3. Prepare Startup Info
            var si = new STARTUPINFOEXW();
            si.StartupInfo.cb = (uint)Marshal.SizeOf<STARTUPINFOEXW>();
            si.StartupInfo.dwFlags = STARTUPINFOW_FLAGS.STARTF_USESTDHANDLES;

            unsafe
            {
                // Initialize Attribute List
                nuint attrListSize = 0;
                PInvoke.InitializeProcThreadAttributeList(default, 1, 0, &attrListSize);
                si.lpAttributeList = (LPPROC_THREAD_ATTRIBUTE_LIST)Marshal.AllocHGlobal((int)attrListSize);
                if (!PInvoke.InitializeProcThreadAttributeList(si.lpAttributeList, 1, 0, &attrListSize))
                    throw new Exception("Failed to initialize attribute list");

                // Use the explicit PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE value
                const nuint PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE = 0x00020016;
                if (!PInvoke.UpdateProcThreadAttribute(si.lpAttributeList, 0, PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE, (void*)hPC.DangerousGetHandle(), (nuint)IntPtr.Size, null, (nuint*)null))
                    throw new Exception("Failed to update attribute list");

                try
                {
                    // 4. Build Command Line
                    var cmd = new StringBuilder();
                    var app = options.App;
                    if (app.Contains(' '))
                        cmd.Append('"').Append(app).Append('"');
                    else
                        cmd.Append(app);

                    foreach (var arg in options.CommandLine)
                    {
                        cmd.Append(' ');
                        if (arg.Contains(' '))
                            cmd.Append('"').Append(arg).Append('"');
                        else
                            cmd.Append(arg);
                    }
                    cmd.Append('\0');

                    // 5. Build Environment
                    byte[] envBlock = null;
                    if (options.Environment != null && options.Environment.Count > 0)
                    {
                        var envBuilder = new StringBuilder();
                        var sortedEnv = new SortedDictionary<string, string>(options.Environment, StringComparer.OrdinalIgnoreCase);
                        foreach (var kv in sortedEnv)
                        {
                            envBuilder.Append(kv.Key).Append('=').Append(kv.Value).Append('\0');
                        }
                        envBuilder.Append('\0');
                        envBlock = Encoding.Unicode.GetBytes(envBuilder.ToString());
                    }

                    // 6. Launch Process
                    char[] cmdBuffer = cmd.ToString().ToCharArray();
                    Span<char> pwCommandLine = new Span<char>(cmdBuffer);
                    fixed (void* pEnv = envBlock)
                    {
                        if (!PInvoke.CreateProcess(
                            null,
                            ref pwCommandLine,
                            null,
                            null,
                            false,
                            PROCESS_CREATION_FLAGS.EXTENDED_STARTUPINFO_PRESENT | PROCESS_CREATION_FLAGS.CREATE_UNICODE_ENVIRONMENT,
                            pEnv,
                            options.Cwd,
                            si.StartupInfo,
                            out var pi))
                        {
                            throw new Exception($"Failed to launch process: {Marshal.GetLastWin32Error()}");
                        }

                        var reader = new FileStream(hReadPipeOurSide, FileAccess.Read, 4096, false);
                        var writer = new FileStream(hWritePipeOurSide, FileAccess.Write, 4096, false);

                        return new WindowsPty(hPC, new SafeProcessHandle(pi.hProcess, true), new SafeThreadHandle(pi.hThread, true), (int)pi.dwProcessId, reader, writer);
                    }
                }
                finally
                {
                    PInvoke.DeleteProcThreadAttributeList(si.lpAttributeList);
                    Marshal.FreeHGlobal((IntPtr)si.lpAttributeList);
                }
            }
        }

        public void Resize(int cols, int rows)
        {
            if (_isDisposed)
                return;
            var size = new COORD { X = (short)cols, Y = (short)rows };
            PInvoke.ResizePseudoConsole(_hPC, size);
        }

        public void Kill()
        {
            if (_isDisposed)
                return;
            PInvoke.TerminateProcess(_hProcess, 1);
        }

        public bool WaitForExit(int milliseconds)
        {
            return PInvoke.WaitForSingleObject(_hProcess, (uint)milliseconds) == WAIT_EVENT.WAIT_OBJECT_0;
        }

        public void Dispose()
        {
            if (_isDisposed)
                return;
            _isDisposed = true;

            ReaderStream.Dispose();
            WriterStream.Dispose();
            _hPC.Dispose();
            _hThread.Dispose();
            _hProcess.Dispose();
        }
    }

    internal class SafeThreadHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public SafeThreadHandle() : base(true) { }
        public SafeThreadHandle(IntPtr handle, bool ownsHandle) : base(ownsHandle) { SetHandle(handle); }
        protected override bool ReleaseHandle() => PInvoke.CloseHandle((HANDLE)handle);
    }
#pragma warning restore CA1416
#endif
}
