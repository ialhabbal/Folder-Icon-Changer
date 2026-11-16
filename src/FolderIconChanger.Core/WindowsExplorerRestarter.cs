using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace FolderIconChanger.Core;

public sealed class WindowsExplorerRestarter
{
    private const int RestartTimeoutMilliseconds = 15_000;

    public Task RestartAsync()
    {
        return Task.Run(Restart);
    }

    private static void Restart()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "Windows Explorer can be restarted only on Windows.");
        }

        var shellWindow = GetShellWindow();
        if (shellWindow != IntPtr.Zero)
        {
            _ = GetWindowThreadProcessId(shellWindow, out var processId);
            if (processId == 0)
            {
                throw new FolderIconOperationException(
                    "Windows Explorer could not be restarted. Please restart it manually.");
            }

            Process? shellProcess;
            try
            {
                shellProcess = Process.GetProcessById((int)processId);
            }
            catch (ArgumentException)
            {
                shellProcess = null;
            }

            if (shellProcess is not null)
            {
                using (shellProcess)
                {
                    if (!string.Equals(
                            shellProcess.ProcessName,
                            "explorer",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        throw new FolderIconOperationException(
                            "The Windows shell is not running as Explorer, so it was not restarted.");
                    }

                    try
                    {
                        if (!shellProcess.HasExited)
                        {
                            try
                            {
                                shellProcess.Kill();
                            }
                            catch (InvalidOperationException) when (shellProcess.HasExited)
                            {
                            }

                            if (!shellProcess.WaitForExit(RestartTimeoutMilliseconds))
                            {
                                throw new FolderIconOperationException(
                                    "Windows Explorer did not stop in time. Please restart it manually.");
                            }
                        }
                    }
                    catch (Exception exception) when (
                        exception is InvalidOperationException or Win32Exception)
                    {
                        throw new FolderIconOperationException(
                            "Windows Explorer could not be stopped. Please restart it manually.",
                            exception);
                    }
                }
            }
        }

        if (GetShellWindow() == IntPtr.Zero)
        {
            StartExplorer();
        }

        WaitForShell();
    }

    private static void StartExplorer()
    {
        var windowsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var explorerPath = Path.Combine(windowsDirectory, "explorer.exe");
        if (!File.Exists(explorerPath))
        {
            throw new FolderIconOperationException(
                "Windows Explorer could not be found and was not restarted.");
        }

        try
        {
            using var explorerProcess = Process.Start(new ProcessStartInfo
            {
                FileName = explorerPath,
                UseShellExecute = false
            });
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or Win32Exception)
        {
            throw new FolderIconOperationException(
                "Windows Explorer could not be started. Please start it manually.",
                exception);
        }
    }

    private static void WaitForShell()
    {
        var timer = Stopwatch.StartNew();
        while (GetShellWindow() == IntPtr.Zero)
        {
            if (timer.ElapsedMilliseconds >= RestartTimeoutMilliseconds)
            {
                throw new FolderIconOperationException(
                    "Windows Explorer did not return after restarting. Please start it manually.");
            }

            Thread.Sleep(100);
        }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetShellWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
}
