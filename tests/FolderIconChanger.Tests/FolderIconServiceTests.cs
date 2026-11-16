using System.Text;
using FolderIconChanger.Core;
using ImageMagick;
using Xunit;

namespace FolderIconChanger.Tests;

public sealed class FolderIconServiceTests
{
    [Fact]
    public async Task ApplyAndRestore_PreserveExistingUnicodeSettingsAndNeverReplaceAnIconFile()
    {
        using var folder = new TemporaryDirectoryOnProjectDrive();
        using var image = new TemporaryDirectory();
        var iniPath = Path.Combine(folder.Path, "desktop.ini");
        var existingIconPath = Path.Combine(folder.Path, ".foldericon.ico");
        var imagePath = Path.Combine(image.Path, "image with spaces.png");
        var existingIconBytes = new byte[] { 1, 2, 3, 4, 5 };
        var originalText = "[.ShellClassInfo]\r\n"
            + "LocalizedResourceName=مجلدي\r\n"
            + "IconResource=old custom.ico,2\r\n"
            + "[Other]\r\n"
            + "Keep=إعدادات المستخدم\r\n";

        File.WriteAllBytes(
            iniPath,
            new UnicodeEncoding(false, true, true).GetPreamble()
                .Concat(new UnicodeEncoding(false, false, true).GetBytes(originalText))
                .ToArray());
        File.SetAttributes(iniPath, FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReadOnly);
        File.WriteAllBytes(existingIconPath, existingIconBytes);
        using (var source = new MagickImage(MagickColors.Orange, 140, 70))
        {
            source.Write(imagePath, MagickFormat.Png);
        }

        var service = new FolderIconService();
        var applied = await service.ApplyAsync(folder.Path, imagePath);
        var appliedText = File.ReadAllText(iniPath, Encoding.Unicode);

        Assert.Contains("IconResource=" + applied.IconFileName + ",0", appliedText);
        Assert.Contains("LocalizedResourceName=مجلدي", appliedText);
        Assert.Contains("Keep=إعدادات المستخدم", appliedText);
        Assert.Equal(existingIconBytes, File.ReadAllBytes(existingIconPath));
        Assert.True((File.GetAttributes(folder.Path) & FileAttributes.System) != 0);

        var restored = await service.RestoreAsync(folder.Path);
        var restoredText = File.ReadAllText(iniPath, Encoding.Unicode);

        Assert.True(restored, restoredText);
        Assert.Contains("IconResource=old custom.ico,2", restoredText);
        Assert.Contains("LocalizedResourceName=مجلدي", restoredText);
        Assert.Contains("Keep=إعدادات المستخدم", restoredText);
        Assert.Equal(
            FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReadOnly,
            File.GetAttributes(iniPath));
        Assert.Equal(existingIconBytes, File.ReadAllBytes(existingIconPath));
        Assert.False((File.GetAttributes(folder.Path) & FileAttributes.System) != 0);
        Assert.True(File.Exists(Path.Combine(folder.Path, applied.IconFileName)));
    }

    [Fact]
    public async Task ApplyAndRestore_AcrossDrives_KeepTheFolderAndRemoveOnlyTheCreatedDesktopIni()
    {
        using var folder = new TemporaryDirectoryOnProjectDrive();
        using var image = new TemporaryDirectory();
        var imagePath = Path.Combine(image.Path, "picture.png");
        using (var source = new MagickImage(MagickColors.CornflowerBlue, 96, 48))
        {
            source.Write(imagePath, MagickFormat.Png);
        }

        Assert.NotEqual(
            Path.GetPathRoot(folder.Path),
            Path.GetPathRoot(imagePath),
            StringComparer.OrdinalIgnoreCase);

        var service = new FolderIconService();
        var result = await service.ApplyAsync(folder.Path, imagePath);

        Assert.True(File.Exists(Path.Combine(folder.Path, "desktop.ini")));
        Assert.True(File.Exists(Path.Combine(folder.Path, result.IconFileName)));
        Assert.True(await service.RestoreAsync(folder.Path));
        Assert.False(File.Exists(Path.Combine(folder.Path, "desktop.ini")));
        Assert.True(File.Exists(Path.Combine(folder.Path, result.IconFileName)));
        Assert.False((File.GetAttributes(folder.Path) & FileAttributes.System) != 0);
    }

    [Fact]
    public async Task ApplyAndRestore_HandleArabicFolderNamesAndImagePathsWithSpaces()
    {
        using var parent = new TemporaryDirectoryOnProjectDrive();
        using var image = new TemporaryDirectory();
        var target = Path.Combine(parent.Path, "مجلد عربي with spaces");
        var imagePath = Path.Combine(image.Path, "صورة عربية with spaces.png");
        Directory.CreateDirectory(target);
        using (var source = new MagickImage(MagickColors.Orange, 140, 70))
        {
            source.Write(imagePath, MagickFormat.Png);
        }

        var service = new FolderIconService();
        var result = await service.ApplyAsync(target, imagePath);

        Assert.True(File.Exists(Path.Combine(target, "desktop.ini")));
        Assert.True(File.Exists(Path.Combine(target, result.IconFileName)));
        Assert.True(await service.RestoreAsync(target));
        Assert.False(File.Exists(Path.Combine(target, "desktop.ini")));
        Assert.True(Directory.Exists(target));
    }

    [Fact]
    public async Task Apply_DoesNotChangeUnrelatedDesktopIniSettings()
    {
        using var folder = new TemporaryDirectoryOnProjectDrive();
        using var image = new TemporaryDirectory();
        var iniPath = Path.Combine(folder.Path, "desktop.ini");
        var imagePath = Path.Combine(image.Path, "picture.png");
        var original = "[Other]\nKeepThis=untouched\n";
        File.WriteAllText(iniPath, original, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        using (var source = new MagickImage(MagickColors.Green, 80, 80))
        {
            source.Write(imagePath, MagickFormat.Png);
        }

        await new FolderIconService().ApplyAsync(folder.Path, imagePath);
        var updated = File.ReadAllText(iniPath);

        Assert.Contains("KeepThis=untouched", updated);
        Assert.Contains("[Other]", updated);
    }

    [Fact]
    public async Task ApplyAndRestore_PreserveNormalDesktopIniAttributes()
    {
        using var folder = new TemporaryDirectoryOnProjectDrive();
        using var image = new TemporaryDirectory();
        var iniPath = Path.Combine(folder.Path, "desktop.ini");
        var imagePath = Path.Combine(image.Path, "picture.png");
        File.WriteAllText(iniPath, "[Other]\r\nKeep=preserved\r\n");
        File.SetAttributes(iniPath, FileAttributes.Normal);
        using (var source = new MagickImage(MagickColors.ForestGreen, 80, 80))
        {
            source.Write(imagePath, MagickFormat.Png);
        }

        var service = new FolderIconService();
        await service.ApplyAsync(folder.Path, imagePath);

        Assert.Equal(
            FileAttributes.Hidden | FileAttributes.System,
            File.GetAttributes(iniPath));

        Assert.True(await service.RestoreAsync(folder.Path));
        Assert.Equal(FileAttributes.Normal, File.GetAttributes(iniPath));
        Assert.Contains("Keep=preserved", File.ReadAllText(iniPath));
    }

    [Fact]
    public async Task Restore_MigratesVersionOneMetadataWithoutChangingOriginalReadOnlyState()
    {
        using var folder = new TemporaryDirectoryOnProjectDrive();
        using var image = new TemporaryDirectory();
        var iniPath = Path.Combine(folder.Path, "desktop.ini");
        var imagePath = Path.Combine(image.Path, "picture.png");
        using (var source = new MagickImage(MagickColors.SlateBlue, 80, 80))
        {
            source.Write(imagePath, MagickFormat.Png);
        }

        var service = new FolderIconService();
        await service.ApplyAsync(folder.Path, imagePath);

        var folderAttributes = File.GetAttributes(folder.Path);
        File.SetAttributes(folder.Path, folderAttributes & ~FileAttributes.ReadOnly);
        var iniText = File.ReadAllText(iniPath, Encoding.Unicode)
            .Replace("Version=2", "Version=1", StringComparison.Ordinal)
            .Replace(
                "OriginalFolderReadOnly=False\r\n",
                string.Empty,
                StringComparison.Ordinal);
        File.SetAttributes(iniPath, FileAttributes.Normal);
        File.WriteAllText(iniPath, iniText, new UnicodeEncoding(false, true));
        File.SetAttributes(iniPath, FileAttributes.Hidden | FileAttributes.System);

        Assert.True(await service.RestoreAsync(folder.Path));
        Assert.False((File.GetAttributes(folder.Path)
            & (FileAttributes.System | FileAttributes.ReadOnly)) != 0);
    }

    [Fact]
    public async Task ApplyAndRestore_PreserveAnOriginalReadOnlyFolderAttribute()
    {
        using var folder = new TemporaryDirectoryOnProjectDrive();
        using var image = new TemporaryDirectory();
        var imagePath = Path.Combine(image.Path, "picture.png");
        using (var source = new MagickImage(MagickColors.SteelBlue, 80, 80))
        {
            source.Write(imagePath, MagickFormat.Png);
        }

        var initialAttributes = File.GetAttributes(folder.Path);
        File.SetAttributes(folder.Path, initialAttributes | FileAttributes.ReadOnly);
        var service = new FolderIconService();

        await service.ApplyAsync(folder.Path, imagePath);
        Assert.True((File.GetAttributes(folder.Path) & FileAttributes.ReadOnly) != 0);
        Assert.True((File.GetAttributes(folder.Path) & FileAttributes.System) != 0);

        Assert.True(await service.RestoreAsync(folder.Path));
        var restoredAttributes = File.GetAttributes(folder.Path);
        Assert.True((restoredAttributes & FileAttributes.ReadOnly) != 0);
        Assert.False((restoredAttributes & FileAttributes.System) != 0);
    }

    [Fact]
    public async Task ApplyAndRestore_PreserveAnExistingIniWithoutShellClassInfoOrLineFeedCharacters()
    {
        using var folder = new TemporaryDirectoryOnProjectDrive();
        using var image = new TemporaryDirectory();
        var iniPath = Path.Combine(folder.Path, "desktop.ini");
        var imagePath = Path.Combine(image.Path, "picture.png");
        var originalBytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)
            .GetBytes("[Other]\rKeep=preserved\r");
        File.WriteAllBytes(iniPath, originalBytes);
        using (var source = new MagickImage(MagickColors.IndianRed, 80, 80))
        {
            source.Write(imagePath, MagickFormat.Png);
        }

        var service = new FolderIconService();
        await service.ApplyAsync(folder.Path, imagePath);
        Assert.True(await service.RestoreAsync(folder.Path));

        Assert.Equal(originalBytes, File.ReadAllBytes(iniPath));
    }

    [Fact]
    public async Task Restore_PreservesAnIconSettingChangedByTheUserAfterApplying()
    {
        using var folder = new TemporaryDirectoryOnProjectDrive();
        using var image = new TemporaryDirectory();
        var iniPath = Path.Combine(folder.Path, "desktop.ini");
        var imagePath = Path.Combine(image.Path, "picture.png");
        using (var source = new MagickImage(MagickColors.MediumPurple, 80, 80))
        {
            source.Write(imagePath, MagickFormat.Png);
        }

        var service = new FolderIconService();
        await service.ApplyAsync(folder.Path, imagePath);
        var text = File.ReadAllText(iniPath, Encoding.Unicode);
        text = text.Replace(
            "IconResource=",
            "IconResource=user-selected.ico,5\r\n; previous value: ",
            StringComparison.Ordinal);
        File.SetAttributes(iniPath, FileAttributes.Normal);
        File.WriteAllText(iniPath, text, new UnicodeEncoding(false, true));
        File.SetAttributes(iniPath, FileAttributes.Hidden | FileAttributes.System);

        Assert.True(await service.RestoreAsync(folder.Path));

        var restoredText = File.ReadAllText(iniPath, Encoding.Unicode);
        Assert.Contains("IconResource=user-selected.ico,5", restoredText);
        Assert.DoesNotContain("[FolderIconChanger]", restoredText);
        Assert.True((File.GetAttributes(folder.Path) & FileAttributes.System) != 0);
    }

    [Fact]
    public async Task Restore_KeepsAUserSettingAddedToANewDesktopIni()
    {
        using var folder = new TemporaryDirectoryOnProjectDrive();
        using var image = new TemporaryDirectory();
        var iniPath = Path.Combine(folder.Path, "desktop.ini");
        var imagePath = Path.Combine(image.Path, "picture.png");
        using (var source = new MagickImage(MagickColors.Teal, 80, 80))
        {
            source.Write(imagePath, MagickFormat.Png);
        }

        var service = new FolderIconService();
        await service.ApplyAsync(folder.Path, imagePath);
        var text = File.ReadAllText(iniPath, Encoding.Unicode);
        text = text.Replace(
            "[FolderIconChanger]",
            "LocalizedResourceName=My folder\r\n[FolderIconChanger]",
            StringComparison.Ordinal);
        File.SetAttributes(iniPath, FileAttributes.Normal);
        File.WriteAllText(iniPath, text, new UnicodeEncoding(false, true));
        File.SetAttributes(iniPath, FileAttributes.Hidden | FileAttributes.System);

        Assert.True(await service.RestoreAsync(folder.Path));

        var restoredText = File.ReadAllText(iniPath, Encoding.Unicode);
        Assert.True(File.Exists(iniPath));
        Assert.Contains("LocalizedResourceName=My folder", restoredText);
        Assert.DoesNotContain("[FolderIconChanger]", restoredText);
        Assert.False((File.GetAttributes(folder.Path) & FileAttributes.System) != 0);
    }

    [Fact]
    public async Task Apply_DoesNotReplaceAnUnrelatedReservedDesktopIniSection()
    {
        using var folder = new TemporaryDirectoryOnProjectDrive();
        using var image = new TemporaryDirectory();
        var iniPath = Path.Combine(folder.Path, "desktop.ini");
        var imagePath = Path.Combine(image.Path, "picture.png");
        var original = "[Other]\r\nKeep=untouched\r\n"
            + "[FolderIconChanger]\r\nOwner=another program\r\n";
        File.WriteAllText(iniPath, original, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        using (var source = new MagickImage(MagickColors.Gold, 80, 80))
        {
            source.Write(imagePath, MagickFormat.Png);
        }

        var beforeFiles = Directory.GetFiles(folder.Path).Order(StringComparer.Ordinal).ToArray();

        await Assert.ThrowsAsync<FolderIconOperationException>(
            () => new FolderIconService().ApplyAsync(folder.Path, imagePath));

        Assert.Equal(original, File.ReadAllText(iniPath));
        Assert.Equal(beforeFiles, Directory.GetFiles(folder.Path).Order(StringComparer.Ordinal));
    }
}
