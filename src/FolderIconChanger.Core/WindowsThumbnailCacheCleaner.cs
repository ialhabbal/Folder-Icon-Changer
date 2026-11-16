namespace FolderIconChanger.Core;

public sealed class WindowsThumbnailCacheCleaner
{
    private const string ThumbnailCachePattern = "thumbcache_*.db";
    private readonly string? _cacheDirectory;

    public WindowsThumbnailCacheCleaner()
    {
    }

    public WindowsThumbnailCacheCleaner(string cacheDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheDirectory);
        _cacheDirectory = Path.GetFullPath(cacheDirectory);
    }

    public Task<int> ClearAsync()
    {
        return Task.Run(Clear);
    }

    private int Clear()
    {
        var cacheDirectory = _cacheDirectory ?? GetCurrentUserCacheDirectory();
        string[] cacheFiles;
        try
        {
            cacheFiles = Directory.GetFiles(
                cacheDirectory,
                ThumbnailCachePattern,
                SearchOption.TopDirectoryOnly);
        }
        catch (DirectoryNotFoundException)
        {
            return 0;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            throw new FolderIconOperationException(
                "Windows could not access the current user's thumbnail cache.",
                exception);
        }

        var deletedCount = 0;
        var deletionErrors = new List<Exception>();
        foreach (var cacheFile in cacheFiles)
        {
            try
            {
                File.Delete(cacheFile);
                deletedCount++;
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
                deletionErrors.Add(new IOException(
                    $"Could not delete '{Path.GetFileName(cacheFile)}'.",
                    exception));
            }
        }

        if (deletionErrors.Count > 0)
        {
            var message =
                $"Windows deleted {deletedCount} thumbnail cache file(s), but could not delete {deletionErrors.Count}. " +
                "Close apps using thumbnails and try again.";
            throw new FolderIconOperationException(
                message,
                new AggregateException(deletionErrors));
        }

        return deletedCount;
    }

    private static string GetCurrentUserCacheDirectory()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localAppData))
        {
            throw new FolderIconOperationException(
                "Windows could not locate the current user's thumbnail cache.");
        }

        return Path.Combine(localAppData, "Microsoft", "Windows", "Explorer");
    }
}
