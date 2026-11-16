using System.Text;
using FolderIconChanger.Core;
using ImageMagick;
using Xunit;

namespace FolderIconChanger.Tests;

public sealed class FolderIconWorkflowTests
{
    [Fact]
    public async Task ApplyAsync_ReportsSuccessOnlyAfterVerifiedCustomizationAndNotification()
    {
        using var folder = new TemporaryDirectoryOnProjectDrive();
        using var images = new TemporaryDirectory();
        var imagePath = CreateImage(images.Path, "picture.png", MagickColors.CornflowerBlue);
        var notifier = new RecordingNotifier();
        var workflow = new FolderIconWorkflow(new FolderIconService(changeNotifier: notifier));
        var successReported = false;
        FolderIconResult? callbackResult = null;

        var result = await workflow.ApplyAsync(
            folder.Path,
            imagePath,
            applied =>
            {
                successReported = true;
                callbackResult = applied;
                Assert.Equal(1, notifier.NotificationCount);

                var iniPath = Path.Combine(folder.Path, "desktop.ini");
                var iniText = File.ReadAllText(iniPath, Encoding.Unicode);
                Assert.Contains("IconResource=" + applied.IconFileName + ",0", iniText);
                Assert.True(
                    (File.GetAttributes(folder.Path)
                        & (FileAttributes.System | FileAttributes.ReadOnly))
                    == (FileAttributes.System | FileAttributes.ReadOnly));
                Assert.True((File.GetAttributes(iniPath) & (FileAttributes.Hidden | FileAttributes.System))
                    == (FileAttributes.Hidden | FileAttributes.System));

                var iconPath = Path.Combine(folder.Path, applied.IconFileName);
                Assert.True(File.Exists(iconPath));
                new IconFileBuilder().Validate(iconPath);
            });

        Assert.True(successReported);
        Assert.Equal(result, callbackResult);
        Assert.Equal(1, notifier.NotificationCount);
        Assert.True(File.Exists(Path.Combine(folder.Path, result.IconFileName)));
    }

    [Fact]
    public async Task ApplyAsync_DoesNotReportSuccessWhenImageConversionFails()
    {
        using var folder = new TemporaryDirectoryOnProjectDrive();
        using var images = new TemporaryDirectory();
        var imagePath = Path.Combine(images.Path, "not-an-image.jpg");
        File.WriteAllText(imagePath, "invalid image content");
        var notifier = new RecordingNotifier();
        var workflow = new FolderIconWorkflow(new FolderIconService(changeNotifier: notifier));
        var successReported = false;

        await Assert.ThrowsAsync<ImageProcessingException>(() =>
            workflow.ApplyAsync(folder.Path, imagePath, _ => successReported = true));

        Assert.False(successReported);
        Assert.Equal(0, notifier.NotificationCount);
        Assert.False(File.Exists(Path.Combine(folder.Path, "desktop.ini")));
        Assert.Empty(Directory.GetFiles(folder.Path));
    }

    [Fact]
    public async Task ApplyAsync_DoesNotReportSuccessWhenExplorerNotificationFails()
    {
        using var folder = new TemporaryDirectoryOnProjectDrive();
        using var images = new TemporaryDirectory();
        var imagePath = CreateImage(images.Path, "picture.png", MagickColors.DarkOrange);
        var originalAttributes = File.GetAttributes(folder.Path);
        var notifier = new RecordingNotifier
        {
            OnNotify = _ => throw new InvalidOperationException("Simulated shell notification failure.")
        };
        var workflow = new FolderIconWorkflow(new FolderIconService(changeNotifier: notifier));
        var successReported = false;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            workflow.ApplyAsync(folder.Path, imagePath, _ => successReported = true));

        Assert.False(successReported);
        Assert.Equal(originalAttributes, File.GetAttributes(folder.Path));
        Assert.False(File.Exists(Path.Combine(folder.Path, "desktop.ini")));
        Assert.Empty(Directory.GetFiles(folder.Path));
    }

    [Fact]
    public async Task ApplyAsync_RepeatedChangesKeepAValidFinalReferenceAndCanBeRestored()
    {
        using var folder = new TemporaryDirectoryOnProjectDrive();
        using var images = new TemporaryDirectory();
        var firstImage = CreateImage(images.Path, "first.png", MagickColors.MediumPurple);
        var secondImage = CreateImage(images.Path, "second.png", MagickColors.Goldenrod);
        var workflow = new FolderIconWorkflow(
            new FolderIconService(changeNotifier: new RecordingNotifier()));

        var first = await workflow.ApplyAsync(folder.Path, firstImage);
        var second = await workflow.ApplyAsync(folder.Path, secondImage);

        Assert.NotEqual(first.IconFileName, second.IconFileName);
        Assert.True(File.Exists(Path.Combine(folder.Path, first.IconFileName)));
        Assert.True(File.Exists(Path.Combine(folder.Path, second.IconFileName)));
        var iniText = File.ReadAllText(Path.Combine(folder.Path, "desktop.ini"), Encoding.Unicode);
        Assert.Contains("IconResource=" + second.IconFileName + ",0", iniText);
        new IconFileBuilder().Validate(Path.Combine(folder.Path, second.IconFileName));

        Assert.True(await new FolderIconService(changeNotifier: new RecordingNotifier())
            .RestoreAsync(folder.Path));
        Assert.False(File.Exists(Path.Combine(folder.Path, "desktop.ini")));
        Assert.False((File.GetAttributes(folder.Path)
            & (FileAttributes.System | FileAttributes.ReadOnly)) != 0);
    }

    [Fact]
    public async Task ApplyAsync_SerializesChangesFromSeparateServiceInstances()
    {
        using var folder = new TemporaryDirectoryOnProjectDrive();
        using var images = new TemporaryDirectory();
        var firstImage = CreateImage(images.Path, "first.png", MagickColors.Crimson);
        var secondImage = CreateImage(images.Path, "second.png", MagickColors.DeepSkyBlue);
        var firstWorkflow = new FolderIconWorkflow(
            new FolderIconService(changeNotifier: new RecordingNotifier()));
        var secondWorkflow = new FolderIconWorkflow(
            new FolderIconService(changeNotifier: new RecordingNotifier()));

        var results = await Task.WhenAll(
            firstWorkflow.ApplyAsync(folder.Path, firstImage),
            secondWorkflow.ApplyAsync(folder.Path, secondImage));

        Assert.NotEqual(results[0].IconFileName, results[1].IconFileName);
        var iniText = File.ReadAllText(Path.Combine(folder.Path, "desktop.ini"), Encoding.Unicode);
        var referencedIcon = results.SingleOrDefault(result =>
            iniText.Contains("IconResource=" + result.IconFileName + ",0", StringComparison.Ordinal));
        Assert.NotNull(referencedIcon);
        Assert.True(File.Exists(Path.Combine(folder.Path, referencedIcon.IconFileName)));
        new IconFileBuilder().Validate(Path.Combine(folder.Path, referencedIcon.IconFileName));
    }

    [Fact]
    public async Task RestoreAsync_RollsBackDiskChangesWhenExplorerNotificationFails()
    {
        using var folder = new TemporaryDirectoryOnProjectDrive();
        using var images = new TemporaryDirectory();
        var imagePath = CreateImage(images.Path, "picture.png", MagickColors.DarkSlateGray);
        var applyingService = new FolderIconService(changeNotifier: new RecordingNotifier());
        await applyingService.ApplyAsync(folder.Path, imagePath);
        var iniPath = Path.Combine(folder.Path, "desktop.ini");
        var appliedIniBytes = File.ReadAllBytes(iniPath);
        var appliedFolderAttributes = File.GetAttributes(folder.Path);
        var failingService = new FolderIconService(changeNotifier: new RecordingNotifier
        {
            OnNotify = _ => throw new InvalidOperationException("Simulated shell notification failure.")
        });

        await Assert.ThrowsAsync<InvalidOperationException>(() => failingService.RestoreAsync(folder.Path));

        Assert.Equal(appliedIniBytes, File.ReadAllBytes(iniPath));
        Assert.Equal(appliedFolderAttributes, File.GetAttributes(folder.Path));
        Assert.True(await new FolderIconService(changeNotifier: new RecordingNotifier())
            .RestoreAsync(folder.Path));
        Assert.False(File.Exists(iniPath));
    }

    private static string CreateImage(string directory, string fileName, MagickColor color)
    {
        var path = Path.Combine(directory, fileName);
        using var image = new MagickImage(color, 96, 64);
        image.Write(path, MagickFormat.Png);
        return path;
    }

    private sealed class RecordingNotifier : IFolderIconChangeNotifier
    {
        public int NotificationCount { get; private set; }
        public Action<string>? OnNotify { get; init; }

        public void NotifyFolderChanged(string folderPath)
        {
            NotificationCount++;
            OnNotify?.Invoke(folderPath);
        }
    }
}
