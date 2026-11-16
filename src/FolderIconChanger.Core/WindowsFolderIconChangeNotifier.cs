using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.CSharp.RuntimeBinder;

namespace FolderIconChanger.Core;

public sealed class WindowsFolderIconChangeNotifier : IFolderIconChangeNotifier
{
    private const uint ShcneAttributes = 0x00000800;
    private const uint ShcneUpdateDir = 0x00001000;
    private const uint ShcneUpdateItem = 0x00002000;
    private const uint ShcnfPathW = 0x00000005;
    private const uint ShcnfFlush = 0x00001000;

    public void NotifyFolderChanged(string folderPath)
    {
        const uint flags = ShcnfPathW | ShcnfFlush;
        SHChangeNotify(ShcneAttributes, flags, folderPath, IntPtr.Zero);
        SHChangeNotify(ShcneUpdateDir, flags, folderPath, IntPtr.Zero);
        SHChangeNotify(ShcneUpdateItem, flags, folderPath, IntPtr.Zero);
        RefreshOpenExplorerViews(folderPath);
    }

    private static void RefreshOpenExplorerViews(string folderPath)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "Folder icon changes are supported only on Windows.");
        }

        var refreshPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            NormalizePath(folderPath)
        };
        var parentPath = Directory.GetParent(folderPath)?.FullName;
        if (parentPath is not null)
        {
            refreshPaths.Add(NormalizePath(parentPath));
        }

        var shellType = Type.GetTypeFromProgID("Shell.Application");
        if (shellType is null)
        {
            throw new FolderIconOperationException(
                "Windows Explorer could not refresh the folder view. The icon change was not completed.");
        }

        object? shellApplication = null;
        object? windows = null;
        try
        {
            shellApplication = Activator.CreateInstance(shellType);
            if (shellApplication is null)
            {
                throw new FolderIconOperationException(
                    "Windows Explorer could not refresh the folder view. The icon change was not completed.");
            }

            dynamic shell = shellApplication;
            windows = shell.Windows();
            if (windows is null)
            {
                throw new FolderIconOperationException(
                    "Windows Explorer could not refresh the folder view. The icon change was not completed.");
            }

            dynamic explorerWindows = windows;
            var count = (int)explorerWindows.Count;
            for (var index = 0; index < count; index++)
            {
                object? window = explorerWindows.Item(index);
                if (window is null)
                {
                    continue;
                }

                try
                {
                    dynamic explorerWindow = window;
                    var locationUrl = (string?)explorerWindow.LocationURL;
                    if (!TryGetFileSystemPath(locationUrl, out var locationPath)
                        || !refreshPaths.Contains(locationPath))
                    {
                        continue;
                    }

                    explorerWindow.Refresh();
                }
                finally
                {
                    ReleaseComObject(window);
                }
            }
        }
        catch (Exception exception) when (
            exception is COMException
                or InvalidComObjectException
                or RuntimeBinderException)
        {
            throw new FolderIconOperationException(
                "Windows Explorer could not refresh the folder view. The icon change was not completed.",
                exception);
        }
        finally
        {
            ReleaseComObject(windows);
            ReleaseComObject(shellApplication);
        }
    }

    private static bool TryGetFileSystemPath(string? locationUrl, out string path)
    {
        path = string.Empty;
        if (!Uri.TryCreate(locationUrl, UriKind.Absolute, out var uri) || !uri.IsFile)
        {
            return false;
        }

        path = NormalizePath(uri.LocalPath);
        return true;
    }

    private static string NormalizePath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var rootPath = Path.GetPathRoot(fullPath);
        return string.Equals(fullPath, rootPath, StringComparison.OrdinalIgnoreCase)
            ? fullPath
            : Path.TrimEndingDirectorySeparator(fullPath);
    }

    [SupportedOSPlatform("windows")]
    private static void ReleaseComObject(object? value)
    {
        if (value is not null && Marshal.IsComObject(value))
        {
            Marshal.ReleaseComObject(value);
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern void SHChangeNotify(uint eventId, uint flags, string path, IntPtr item2);
}
