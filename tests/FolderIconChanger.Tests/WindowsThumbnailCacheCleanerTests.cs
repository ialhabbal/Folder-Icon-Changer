using FolderIconChanger.Core;
using Xunit;

namespace FolderIconChanger.Tests;

public sealed class WindowsThumbnailCacheCleanerTests
{
    [Fact]
    public async Task ClearAsync_DeletesOnlyTopLevelThumbnailCacheDatabases()
    {
        using var cache = new TemporaryDirectory();
        var thumbnailCache = Path.Combine(cache.Path, "thumbcache_256.db");
        var thumbnailIndex = Path.Combine(cache.Path, "thumbcache_idx.db");
        var iconCache = Path.Combine(cache.Path, "iconcache_256.db");
        var journalFile = Path.Combine(cache.Path, "thumbcache_256.db-wal");
        var nestedFolder = Directory.CreateDirectory(Path.Combine(cache.Path, "nested"));
        var nestedThumbnailCache = Path.Combine(nestedFolder.FullName, "thumbcache_128.db");
        foreach (var file in new[]
        {
            thumbnailCache,
            thumbnailIndex,
            iconCache,
            journalFile,
            nestedThumbnailCache
        })
        {
            File.WriteAllText(file, "cache");
        }

        var cleaner = new WindowsThumbnailCacheCleaner(cache.Path);
        var deletedCount = await cleaner.ClearAsync();

        Assert.Equal(2, deletedCount);
        Assert.False(File.Exists(thumbnailCache));
        Assert.False(File.Exists(thumbnailIndex));
        Assert.True(File.Exists(iconCache));
        Assert.True(File.Exists(journalFile));
        Assert.True(File.Exists(nestedThumbnailCache));
    }

    [Fact]
    public async Task ClearAsync_ReturnsZeroWhenCacheDirectoryDoesNotExist()
    {
        using var parent = new TemporaryDirectory();
        var missingCache = Path.Combine(parent.Path, "missing");
        var cleaner = new WindowsThumbnailCacheCleaner(missingCache);

        var deletedCount = await cleaner.ClearAsync();

        Assert.Equal(0, deletedCount);
    }
}
