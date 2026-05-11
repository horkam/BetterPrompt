using Microsoft.Win32.SafeHandles;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace BetterPrompt.Services;

/// <summary>
/// Wraps the Windows ConPTY (pseudo-console) API so that child processes see a real TTY.
/// All fields in the P/Invoke structs are blittable (IntPtr instead of string) so that
/// Marshal.SizeOf returns the correct native size and no marshaling copies are made.
/// </summary>
public sealed class ConPtyService : IDisposable
{
    // ── Win32 types (fully blittable) ─────────────────────────────────────────

    [StructLayout(LayoutKind.Sequential)]
    private struct COORD { public short X; public short Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct STARTUPINFOEX
    {
        public STARTUPINFO StartupInfo;
        public IntPtr lpAttributeList;
    }

    // All pointer-sized fields use IntPtr so the struct is blittable and
    // Marshal.SizeOf returns the correct native size (104 bytes on x64).
    [StructLayout(LayoutKind.Sequential)]
    private struct STARTUPINFO
    {
        public int    cb;
        public IntPtr lpReserved;
        public IntPtr lpDesktop;
        public IntPtr lpTitle;
        public int    dwX, dwY, dwXSize, dwYSize;
        public int    dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
        public short  wShowWindow, cbReserved2;
        public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_INFORMATION
    {
        public IntPtr hProcess, hThread;
        public int    dwProcessId, dwThreadId;
    }

    // ── P/Invoke ──────────────────────────────────────────────────────────────

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern int CreatePseudoConsole(
        COORD size, IntPtr hInput, IntPtr hOutput, uint dwFlags, out IntPtr phPC);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern int ResizePseudoConsole(IntPtr hPC, COORD size);

    [DllImport("kernel32.dll")]
    private static extern void ClosePseudoConsole(IntPtr hPC);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateProcess(
        string?       lpApplicationName,
        StringBuilder lpCommandLine,   // must be mutable — CreateProcess may modify the buffer
        IntPtr        lpProcessAttributes,
        IntPtr        lpThreadAttributes,
        bool          bInheritHandles,
        uint          dwCreationFlags,
        IntPtr        lpEnvironment,
        string?       lpCurrentDirectory,
        [In] ref STARTUPINFOEX lpStartupInfo,
        out PROCESS_INFORMATION lpProcessInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool InitializeProcThreadAttributeList(
        IntPtr lpAttributeList, int dwAttributeCount, int dwFlags, ref IntPtr lpSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool UpdateProcThreadAttribute(
        IntPtr lpAttributeList, uint dwFlags, IntPtr Attribute,
        IntPtr lpValue, IntPtr cbSize, IntPtr lpPreviousValue, IntPtr lpReturnSize);

    [DllImport("kernel32.dll")]
    private static extern void DeleteProcThreadAttributeList(IntPtr lpAttributeList);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CreatePipe(
        out SafeFileHandle hReadPipe, out SafeFileHandle hWritePipe,
        IntPtr lpPipeAttributes, int nSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetExitCodeProcess(IntPtr hProcess, out uint lpExitCode);

    private const uint EXTENDED_STARTUPINFO_PRESENT = 0x00080000;
    // ProcThreadAttributeValue(22, Thread=FALSE, Input=TRUE, Additive=FALSE) = 22 | 0x00020000
    private static readonly IntPtr PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE = new(0x00020016);
    private const uint STILL_ACTIVE = 259;

    // ── State ─────────────────────────────────────────────────────────────────

    private IntPtr     _hPC      = IntPtr.Zero;
    private IntPtr     _hProcess = IntPtr.Zero;
    private IntPtr     _hThread  = IntPtr.Zero;
    private FileStream? _inputStream;   // write-side of input pipe
    private FileStream? _outputStream;  // read-side of output pipe (owned here, not by ReadLoop)
    private CancellationTokenSource? _cts;

    public event Action<byte[]>? OutputReceived;
    public event Action?         ProcessExited;
    public bool IsRunning { get; private set; }

    // ── Public API ────────────────────────────────────────────────────────────

    [DllImport("kernel32.dll")] private static extern bool FreeConsole();

    // Prefer pwsh (PowerShell 7+) for better ConPTY support; fall back to Windows PowerShell.
    private static string ResolveShell()
    {
        foreach (var candidate in new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"PowerShell\7\pwsh.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"PowerShell\7-preview\pwsh.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\WindowsApps\pwsh.exe"),
        })
        {
            if (File.Exists(candidate))
            {
                Diag($"ResolveShell: found pwsh at {candidate}");
                return $"\"{candidate}\" -NoProfile -NoLogo";
            }
        }

        Diag("ResolveShell: pwsh not found, falling back to powershell.exe");
        return "powershell.exe -NoProfile -NoLogo";
    }

    private static void Diag(string msg) =>
        System.IO.File.AppendAllText(
            System.IO.Path.Combine(System.IO.Path.GetTempPath(), "BetterPrompt_conpty.log"),
            $"[{DateTime.Now:HH:mm:ss.fff}] {msg}\n");

    public void Start(string workingDirectory, int cols = 120, int rows = 30)
    {
        Stop();
        Diag($"Start() cols={cols} rows={rows} dir={workingDirectory}");

        // 1. Create two anonymous pipes.
        //    input pipe:  we write → ConPTY reads (the shell's stdin)
        //    output pipe: ConPTY writes → we read (the shell's stdout+stderr)
        if (!CreatePipe(out var inputRead, out var inputWrite, IntPtr.Zero, 0))
            throw new InvalidOperationException($"CreatePipe (input) failed: {Marshal.GetLastWin32Error()}");
        Diag("CreatePipe (input) OK");

        if (!CreatePipe(out var outputRead, out var outputWrite, IntPtr.Zero, 0))
        {
            inputRead.Dispose(); inputWrite.Dispose();
            throw new InvalidOperationException($"CreatePipe (output) failed: {Marshal.GetLastWin32Error()}");
        }
        Diag("CreatePipe (output) OK");

        // Note: do NOT call FreeConsole() here — as a GUI (WPF) process we have no
        // console to free, and calling it can interfere with ConPTY initialization.

        // 2. Create pseudo-console — ConPTY duplicates these handles internally.
        var size = new COORD { X = (short)cols, Y = (short)rows };
        int hr = CreatePseudoConsole(
            size,
            inputRead.DangerousGetHandle(),
            outputWrite.DangerousGetHandle(),
            0, out _hPC);

        // Close the ConPTY-side handles — ConPTY has its own duplicates now.
        inputRead.Dispose();
        outputWrite.Dispose();

        Diag($"CreatePseudoConsole hr=0x{hr:X8} hPC=0x{_hPC:X}");
        if (hr != 0)
            throw new InvalidOperationException($"CreatePseudoConsole failed: HRESULT 0x{hr:X8}");

        // 3. Build STARTUPINFOEX with PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE.
        //    hPCStorage pins the ConPTY handle in unmanaged memory; it must stay
        //    allocated from UpdateProcThreadAttribute until after CreateProcess.
        IntPtr attrList = IntPtr.Zero;

        var si = new STARTUPINFOEX();
        si.StartupInfo.cb = Marshal.SizeOf<STARTUPINFOEX>(); // must be sizeof(STARTUPINFOEX)

        try
        {
            IntPtr listSize = IntPtr.Zero;
            InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref listSize);
            attrList = Marshal.AllocHGlobal(listSize);
            si.lpAttributeList = attrList;

            if (!InitializeProcThreadAttributeList(attrList, 1, 0, ref listSize))
                throw new InvalidOperationException($"InitializeProcThreadAttributeList failed: {Marshal.GetLastWin32Error()}");

            // Pass _hPC (the HPCON handle) directly as lpValue — this is what the Win32 API
            // and Microsoft's own EchoCon sample do. Do NOT pass &_hPC (a pointer to the handle).
            if (!UpdateProcThreadAttribute(
                    attrList, 0, PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE,
                    _hPC, (IntPtr)IntPtr.Size,
                    IntPtr.Zero, IntPtr.Zero))
                throw new InvalidOperationException($"UpdateProcThreadAttribute failed: {Marshal.GetLastWin32Error()}");

            // 4. Launch PowerShell inside the pseudo-console.
            // Prefer pwsh (PowerShell 7) which has better ConPTY support; fall back to powershell.exe
            var shell = ResolveShell();
            Diag($"STARTUPINFOEX size={Marshal.SizeOf<STARTUPINFOEX>()} cb={si.StartupInfo.cb} shell={shell}");
            var shellBuf = new StringBuilder(shell, shell.Length + 1);

            if (!CreateProcess(
                    null, shellBuf,
                    IntPtr.Zero, IntPtr.Zero,
                    false,                          // must NOT inherit handles
                    EXTENDED_STARTUPINFO_PRESENT,
                    IntPtr.Zero,
                    workingDirectory,
                    ref si,
                    out var pi))
                throw new InvalidOperationException($"CreateProcess failed: {Marshal.GetLastWin32Error()}");

            Diag($"CreateProcess OK pid={pi.dwProcessId}");
            _hProcess = pi.hProcess;
            _hThread  = pi.hThread;
        }
        finally
        {
            if (attrList != IntPtr.Zero)
            {
                DeleteProcThreadAttributeList(attrList);
                Marshal.FreeHGlobal(attrList);
            }
        }

        // 5. Wrap our sides of the pipes in FileStream. These streams own the handles.
        _inputStream  = new FileStream(inputWrite,  FileAccess.Write, bufferSize: 1,    isAsync: false);
        _outputStream = new FileStream(outputRead,  FileAccess.Read,  bufferSize: 4096, isAsync: false);

        IsRunning = true;
        _cts = new CancellationTokenSource();
        Task.Run(() => ReadLoop(_cts.Token));
        Task.Run(() => MonitorExitAsync(_cts.Token));
    }

    public void Resize(int cols, int rows)
    {
        if (_hPC != IntPtr.Zero)
            ResizePseudoConsole(_hPC, new COORD { X = (short)cols, Y = (short)rows });
    }

    public void Write(byte[] data)
    {
        if (_inputStream is null || !IsRunning) return;
        try { lock (_inputStream) { _inputStream.Write(data); _inputStream.Flush(); } }
        catch { /* process exited */ }
    }

    public void Write(string text) => Write(Encoding.UTF8.GetBytes(text));

    public void Stop()
    {
        IsRunning = false;
        _cts?.Cancel();
        _cts = null;

        // Disposing the output stream unblocks the blocking Read() in ReadLoop.
        _outputStream?.Dispose();
        _outputStream = null;

        _inputStream?.Dispose();
        _inputStream = null;

        if (_hPC      != IntPtr.Zero) { ClosePseudoConsole(_hPC); _hPC      = IntPtr.Zero; }
        if (_hProcess != IntPtr.Zero) { CloseHandle(_hProcess);   _hProcess = IntPtr.Zero; }
        if (_hThread  != IntPtr.Zero) { CloseHandle(_hThread);    _hThread  = IntPtr.Zero; }
    }

    public void Dispose() => Stop();

    // ── Private ───────────────────────────────────────────────────────────────

    private void ReadLoop(CancellationToken ct)
    {
        var buf = new byte[4096];
        try
        {
            int n;
            while (!ct.IsCancellationRequested && (n = _outputStream!.Read(buf, 0, buf.Length)) > 0)
            {
                var data = new byte[n];
                Buffer.BlockCopy(buf, 0, data, 0, n);
                OutputReceived?.Invoke(data);
            }
        }
        catch { /* pipe closed when Stop() disposes _outputStream — normal exit */ }
    }

    private async Task MonitorExitAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(500, ct).ConfigureAwait(false);
                if (_hProcess == IntPtr.Zero) break;
                if (!GetExitCodeProcess(_hProcess, out uint code) || code != STILL_ACTIVE)
                {
                    Diag($"Process exited with code 0x{code:X8} ({code})");
                    IsRunning = false;
                    ProcessExited?.Invoke();
                    break;
                }
            }
        }
        catch (OperationCanceledException) { }
    }
}
