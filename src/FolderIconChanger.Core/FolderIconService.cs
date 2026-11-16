using System.Globalization;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FolderIconChanger.Core;

public sealed class FolderIconService
{
    private const string IniFileName = "desktop.ini";
    private const string MetadataSection = "FolderIconChanger";
    private const string MetadataOwner = "8C68324F-6E51-4429-9D60-54075D84EB02";
    private const int CurrentMetadataVersion = 2;
    private static readonly string[] IconKeys = ["IconResource", "IconFile", "IconIndex"];
    private static readonly Regex OwnedIconFileName =
        new(@"^\.foldericon-[0-9a-f]{32}\.ico$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private readonly IconFileBuilder _iconFileBuilder;
    private readonly IFolderIconChangeNotifier _changeNotifier;

    public FolderIconService(
        IconFileBuilder? iconFileBuilder = null,
        IFolderIconChangeNotifier? changeNotifier = null)
    {
        _iconFileBuilder = iconFileBuilder ?? new IconFileBuilder();
        _changeNotifier = changeNotifier ?? new WindowsFolderIconChangeNotifier();
    }

    public Task<FolderIconResult> ApplyAsync(string folderPath, string imagePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(imagePath);
        return Task.Run(() => Apply(folderPath, imagePath));
    }

    public Task<bool> RestoreAsync(string folderPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderPath);
        return Task.Run(() => Restore(folderPath));
    }

    private FolderIconResult Apply(string folderPath, string imagePath)
    {
        var folder = NormalizeFolderPath(folderPath);
        if (!Directory.Exists(folder))
        {
            throw new FolderIconOperationException("The selected folder could not be found.");
        }

        var image = Path.GetFullPath(imagePath);
        if (!File.Exists(image))
        {
            throw new ImageProcessingException(
                "The selected image could not be opened or converted.",
                new FileNotFoundException("The selected image could not be found.", image));
        }

        using var folderLock = AcquireFolderLock(folder);
        return ApplyLocked(folder, image);
    }

    private FolderIconResult ApplyLocked(string folder, string imagePath)
    {
        var iconBytes = _iconFileBuilder.Build(imagePath);
        var identifier = Guid.NewGuid().ToString("N");
        var iconFileName = ".foldericon-" + identifier + ".ico";
        var iconPath = Path.Combine(folder, iconFileName);
        var temporaryIconPath = Path.Combine(folder, ".foldericon-" + identifier + ".tmp");
        var iniPath = Path.Combine(folder, IniFileName);
        var folderAttributes = File.GetAttributes(folder);
        byte[]? originalIniBytes = null;
        byte[]? updatedIniBytes = null;
        FileAttributes originalIniAttributes = 0;
        var iniExisted = false;
        var iniUpdated = false;
        var iconCreated = false;
        var folderAttributesChanged = false;

        try
        {
            WriteNewIcon(temporaryIconPath, iconBytes);
            _iconFileBuilder.Validate(temporaryIconPath);
            if (!File.ReadAllBytes(temporaryIconPath).AsSpan().SequenceEqual(iconBytes))
            {
                throw new FolderIconOperationException(
                    "Windows could not verify the completed icon file. No folder settings were changed.");
            }

            File.Move(temporaryIconPath, iconPath);
            iconCreated = true;
            _iconFileBuilder.Validate(iconPath);
            if (!File.ReadAllBytes(iconPath).AsSpan().SequenceEqual(iconBytes))
            {
                throw new FolderIconOperationException(
                    "Windows could not verify the completed icon file. No folder settings were changed.");
            }

            iniExisted = File.Exists(iniPath);
            originalIniBytes = iniExisted ? File.ReadAllBytes(iniPath) : null;
            originalIniAttributes = iniExisted ? File.GetAttributes(iniPath) : 0;
            var document = DesktopIniDocument.Read(iniPath, iniExisted, originalIniBytes);
            var state = ReadState(
                document.Lines,
                iniExisted,
                originalIniAttributes,
                folderAttributes);
            var createdShellClassInfo = state.CreatedShellClassInfo
                || DesktopIniDocument.FindSection(document.Lines, ".ShellClassInfo") is null;
            var addedShellClassInfoSeparator = state.AddedShellClassInfoSeparator
                || createdShellClassInfo
                    && document.Lines.Count > 0
                    && !string.IsNullOrWhiteSpace(document.Lines[^1]);

            DesktopIniDocument.SetCurrentIcon(document.Lines, iconFileName);
            WriteState(
                document.Lines,
                state with
                {
                    CurrentIconFile = iconFileName,
                    CreatedShellClassInfo = createdShellClassInfo,
                    AddedShellClassInfoSeparator = addedShellClassInfoSeparator
                });

            updatedIniBytes = document.ToBytes();
            File.SetAttributes(
                folder,
                WithRequiredAttributes(
                    folderAttributes,
                    FileAttributes.System | FileAttributes.ReadOnly));
            folderAttributesChanged = true;

            WriteDocument(iniPath, originalIniBytes, updatedIniBytes, originalIniAttributes);
            iniUpdated = true;
            VerifyCustomization(folder, iconPath, iconFileName, iconBytes, updatedIniBytes);
            _changeNotifier.NotifyFolderChanged(folder);
            VerifyCustomization(folder, iconPath, iconFileName, iconBytes, updatedIniBytes);
            return new FolderIconResult(iconFileName);
        }
        catch (Exception operationException)
        {
            var rollbackErrors = new List<Exception>();
            if (iniUpdated && updatedIniBytes is not null)
            {
                try
                {
                    RollBackDesktopIni(
                        iniPath,
                        updatedIniBytes,
                        originalIniBytes,
                        originalIniAttributes);
                }
                catch (Exception rollbackException)
                {
                    rollbackErrors.Add(rollbackException);
                }
            }

            if (folderAttributesChanged)
            {
                try
                {
                    File.SetAttributes(folder, folderAttributes);
                }
                catch (Exception rollbackException)
                {
                    rollbackErrors.Add(rollbackException);
                }
            }

            if (iconCreated && File.Exists(iconPath))
            {
                try
                {
                    File.Delete(iconPath);
                }
                catch (Exception rollbackException)
                {
                    rollbackErrors.Add(rollbackException);
                }
            }

            if (File.Exists(temporaryIconPath))
            {
                try
                {
                    File.Delete(temporaryIconPath);
                }
                catch (Exception rollbackException)
                {
                    rollbackErrors.Add(rollbackException);
                }
            }

            if (rollbackErrors.Count > 0)
            {
                rollbackErrors.Insert(0, operationException);
                throw new FolderIconOperationException(
                    "Windows could not safely finish or roll back the icon change. Please check the folder before trying again.",
                    new AggregateException(rollbackErrors));
            }

            ExceptionDispatchInfo.Capture(operationException).Throw();
            throw;
        }
    }

    private bool Restore(string folderPath)
    {
        var folder = NormalizeFolderPath(folderPath);
        if (!Directory.Exists(folder))
        {
            throw new FolderIconOperationException("The selected folder could not be found.");
        }

        using var folderLock = AcquireFolderLock(folder);
        return RestoreLocked(folder);
    }

    private bool RestoreLocked(string folder)
    {
        var iniPath = Path.Combine(folder, IniFileName);
        if (!File.Exists(iniPath))
        {
            return false;
        }

        var originalBytes = File.ReadAllBytes(iniPath);
        var originalAttributes = File.GetAttributes(iniPath);
        var folderAttributes = File.GetAttributes(folder);
        var document = DesktopIniDocument.Read(iniPath, true, originalBytes);
        var state = ReadStateIfPresent(document.Lines, folderAttributes);
        if (state is null)
        {
            return false;
        }

        var iconSettingsUnchanged = DesktopIniDocument.RestoreIconKeys(
            document.Lines,
            state.CurrentIconFile,
            state.OriginalIconLines);
        if (state.CreatedShellClassInfo)
        {
            DesktopIniDocument.RemoveEmptySection(
                document.Lines,
                ".ShellClassInfo",
                state.AddedShellClassInfoSeparator);
        }

        DesktopIniDocument.RemoveSection(document.Lines, MetadataSection);

        var deleteCreatedIni = state.CreatedIni
            && !document.HasMeaningfulContentExceptShellClassInfo()
            && !HasShellClassInfoSettings(document.Lines);

        if (deleteCreatedIni)
        {
            if (!File.ReadAllBytes(iniPath).AsSpan().SequenceEqual(originalBytes))
            {
                throw new FolderIconOperationException(
                    "The folder settings changed while the icon was being restored. Try again.");
            }
        }

        var restoreFolderAttributes = iconSettingsUnchanged;
        var restoredFolderAttributes = state.OriginalFolderSystem
            ? folderAttributes | FileAttributes.System
            : folderAttributes & ~FileAttributes.System;
        restoredFolderAttributes = state.OriginalFolderReadOnly
            ? restoredFolderAttributes | FileAttributes.ReadOnly
            : restoredFolderAttributes & ~FileAttributes.ReadOnly;

        byte[]? restoredIniBytes = null;
        var iniUpdated = false;
        var folderAttributesChanged = false;
        try
        {
            if (restoreFolderAttributes)
            {
                File.SetAttributes(folder, restoredFolderAttributes);
                folderAttributesChanged = true;
            }

            if (deleteCreatedIni)
            {
                File.Delete(iniPath);
                iniUpdated = true;
            }
            else
            {
                var restoredIniAttributes = iconSettingsUnchanged && !state.CreatedIni
                    ? (FileAttributes)state.OriginalIniAttributes
                    : originalAttributes;
                restoredIniBytes = document.ToBytes();
                WriteDocument(
                    iniPath,
                    originalBytes,
                    restoredIniBytes,
                    originalAttributes,
                    restoredIniAttributes);
                iniUpdated = true;
            }

            VerifyRestoration(
                folder,
                iniPath,
                state,
                deleteCreatedIni,
                restoreFolderAttributes ? state.OriginalFolderSystem : (folderAttributes & FileAttributes.System) != 0,
                restoreFolderAttributes ? state.OriginalFolderReadOnly : (folderAttributes & FileAttributes.ReadOnly) != 0,
                restoredIniBytes);
            _changeNotifier.NotifyFolderChanged(folder);
            VerifyRestoration(
                folder,
                iniPath,
                state,
                deleteCreatedIni,
                restoreFolderAttributes ? state.OriginalFolderSystem : (folderAttributes & FileAttributes.System) != 0,
                restoreFolderAttributes ? state.OriginalFolderReadOnly : (folderAttributes & FileAttributes.ReadOnly) != 0,
                restoredIniBytes);
        }
        catch (Exception operationException)
        {
            var rollbackErrors = new List<Exception>();
            if (iniUpdated)
            {
                try
                {
                    RollBackRestoreDesktopIni(
                        iniPath,
                        originalBytes,
                        originalAttributes,
                        deleteCreatedIni,
                        restoredIniBytes);
                }
                catch (Exception rollbackException)
                {
                    rollbackErrors.Add(rollbackException);
                }
            }

            if (folderAttributesChanged)
            {
                try
                {
                    File.SetAttributes(folder, folderAttributes);
                }
                catch (Exception rollbackException)
                {
                    rollbackErrors.Add(rollbackException);
                }
            }

            if (rollbackErrors.Count > 0)
            {
                rollbackErrors.Insert(0, operationException);
                throw new FolderIconOperationException(
                    "Windows could not safely finish or roll back the restore operation. Please check the folder before trying again.",
                    new AggregateException(rollbackErrors));
            }

            ExceptionDispatchInfo.Capture(operationException).Throw();
            throw;
        }

        return true;
    }

    private static AppState ReadState(
        List<string> lines,
        bool iniExists,
        FileAttributes iniAttributes,
        FileAttributes folderAttributes)
    {
        var existing = ReadStateIfPresent(lines, folderAttributes);
        if (existing is not null)
        {
            return existing;
        }

        var section = DesktopIniDocument.FindSection(lines, MetadataSection);
        if (section is not null)
        {
            throw new FolderIconOperationException(
                "The folder settings contain an unrelated section with a reserved name. No changes were made.");
        }

        var originalIconLines = DesktopIniDocument.CaptureIconKeys(lines);
        return new AppState(
            string.Empty,
            (folderAttributes & FileAttributes.System) != 0,
            (folderAttributes & FileAttributes.ReadOnly) != 0,
            !iniExists,
            DesktopIniDocument.FindSection(lines, ".ShellClassInfo") is null,
            DesktopIniDocument.FindSection(lines, ".ShellClassInfo") is null
                && lines.Count > 0
                && !string.IsNullOrWhiteSpace(lines[^1]),
            (int)iniAttributes,
            originalIconLines);
    }

    private static AppState? ReadStateIfPresent(
        List<string> lines,
        FileAttributes currentFolderAttributes)
    {
        var section = DesktopIniDocument.FindSection(lines, MetadataSection);
        if (section is null)
        {
            return null;
        }

        var owner = DesktopIniDocument.ReadKey(lines, MetadataSection, "Owner");
        if (!string.Equals(owner, MetadataOwner, StringComparison.Ordinal))
        {
            throw new FolderIconOperationException(
                "The folder settings contain an unrelated section with a reserved name. No changes were made.");
        }

        if (!int.TryParse(
                DesktopIniDocument.ReadKey(lines, MetadataSection, "Version"),
                out var version)
            || version is < 1 or > CurrentMetadataVersion
            || !int.TryParse(
                DesktopIniDocument.ReadKey(lines, MetadataSection, "OriginalIniAttributes"),
                out var iniAttributes)
            || !bool.TryParse(
                DesktopIniDocument.ReadKey(lines, MetadataSection, "OriginalFolderSystem"),
                out var originalFolderSystem)
            || !bool.TryParse(
                DesktopIniDocument.ReadKey(lines, MetadataSection, "CreatedIni"),
                out var createdIni))
        {
            throw new FolderIconOperationException(
                "The folder's saved icon settings could not be read safely. No changes were made.");
        }

        var originalFolderReadOnly = (currentFolderAttributes & FileAttributes.ReadOnly) != 0;
        if (version >= 2
            && !bool.TryParse(
                DesktopIniDocument.ReadKey(lines, MetadataSection, "OriginalFolderReadOnly"),
                out originalFolderReadOnly))
        {
            throw new FolderIconOperationException(
                "The folder's saved icon settings could not be read safely. No changes were made.");
        }

        if (!bool.TryParse(
                DesktopIniDocument.ReadKey(lines, MetadataSection, "CreatedShellClassInfo"),
                out var createdShellClassInfo)
            || !bool.TryParse(
                DesktopIniDocument.ReadKey(lines, MetadataSection, "AddedShellClassInfoSeparator"),
                out var addedShellClassInfoSeparator))
        {
            throw new FolderIconOperationException(
                "The folder's saved icon settings could not be read safely. No changes were made.");
        }

        var currentIconFile = DesktopIniDocument.ReadKey(lines, MetadataSection, "CurrentIconFile");
        if (currentIconFile is null || !OwnedIconFileName.IsMatch(currentIconFile))
        {
            throw new FolderIconOperationException(
                "The folder's saved icon settings could not be read safely. No changes were made.");
        }

        try
        {
            var encodedLines = DesktopIniDocument.ReadKey(lines, MetadataSection, "OriginalIconLines");
            if (encodedLines is null)
            {
                throw new JsonException();
            }

            var json = Encoding.UTF8.GetString(Convert.FromBase64String(encodedLines));
            var originalLines = JsonSerializer.Deserialize<Dictionary<string, List<string>>>(json)
                ?? throw new JsonException();

            if (originalLines.Any(pair =>
                !IconKeys.Contains(pair.Key, StringComparer.OrdinalIgnoreCase)
                || pair.Value.Any(line =>
                    !string.Equals(
                        DesktopIniDocument.ReadKeyName(line),
                        pair.Key,
                        StringComparison.OrdinalIgnoreCase))))
            {
                throw new JsonException();
            }

            return new AppState(
                currentIconFile,
                originalFolderSystem,
                originalFolderReadOnly,
                createdIni,
                createdShellClassInfo,
                addedShellClassInfoSeparator,
                iniAttributes,
                originalLines);
        }
        catch (FormatException exception)
        {
            throw new FolderIconOperationException(
                "The folder's saved icon settings could not be read safely. No changes were made.",
                exception);
        }
        catch (JsonException exception)
        {
            throw new FolderIconOperationException(
                "The folder's saved icon settings could not be read safely. No changes were made.",
                exception);
        }
    }

    private static void WriteState(List<string> lines, AppState state)
    {
        DesktopIniDocument.SetKey(lines, MetadataSection, "Owner", MetadataOwner);
        DesktopIniDocument.SetKey(
            lines,
            MetadataSection,
            "Version",
            CurrentMetadataVersion.ToString(CultureInfo.InvariantCulture));
        DesktopIniDocument.SetKey(
            lines,
            MetadataSection,
            "CurrentIconFile",
            state.CurrentIconFile);
        DesktopIniDocument.SetKey(
            lines,
            MetadataSection,
            "OriginalFolderSystem",
            state.OriginalFolderSystem.ToString(CultureInfo.InvariantCulture));
        DesktopIniDocument.SetKey(
            lines,
            MetadataSection,
            "OriginalFolderReadOnly",
            state.OriginalFolderReadOnly.ToString(CultureInfo.InvariantCulture));
        DesktopIniDocument.SetKey(
            lines,
            MetadataSection,
            "CreatedIni",
            state.CreatedIni.ToString(CultureInfo.InvariantCulture));
        DesktopIniDocument.SetKey(
            lines,
            MetadataSection,
            "CreatedShellClassInfo",
            state.CreatedShellClassInfo.ToString(CultureInfo.InvariantCulture));
        DesktopIniDocument.SetKey(
            lines,
            MetadataSection,
            "AddedShellClassInfoSeparator",
            state.AddedShellClassInfoSeparator.ToString(CultureInfo.InvariantCulture));
        DesktopIniDocument.SetKey(
            lines,
            MetadataSection,
            "OriginalIniAttributes",
            state.OriginalIniAttributes.ToString(CultureInfo.InvariantCulture));
        var originalLines = JsonSerializer.Serialize(state.OriginalIconLines);
        DesktopIniDocument.SetKey(
            lines,
            MetadataSection,
            "OriginalIconLines",
            Convert.ToBase64String(Encoding.UTF8.GetBytes(originalLines)));
    }

    private static void WriteNewIcon(string path, byte[] bytes)
    {
        using (var stream = new FileStream(
                   path,
                   FileMode.CreateNew,
                   FileAccess.Write,
                   FileShare.None,
                   64 * 1024,
                   FileOptions.WriteThrough))
        {
            stream.Write(bytes);
            stream.Flush(flushToDisk: true);
        }

        File.SetAttributes(path, FileAttributes.Hidden);
    }

    private static void WriteDocument(
        string path,
        byte[]? expectedOriginalBytes,
        byte[] updatedBytes,
        FileAttributes originalAttributes,
        FileAttributes? finalAttributes = null)
    {
        var directory = Path.GetDirectoryName(path)
            ?? throw new FolderIconOperationException("Windows could not access this folder.");
        var temporaryPath = Path.Combine(directory, ".foldericon-" + Guid.NewGuid().ToString("N") + ".tmp");
        var targetExisted = expectedOriginalBytes is not null;
        var originalWasReadOnly = (originalAttributes & FileAttributes.ReadOnly) != 0;

        try
        {
            WriteNewFile(temporaryPath, updatedBytes);
            if (!targetExisted)
            {
                File.Move(temporaryPath, path);
                try
                {
                    File.SetAttributes(
                        path,
                        finalAttributes
                            ?? (FileAttributes.Hidden | FileAttributes.System));
                }
                catch
                {
                    if (File.Exists(path)
                        && File.ReadAllBytes(path).AsSpan().SequenceEqual(updatedBytes))
                    {
                        File.Delete(path);
                    }

                    throw;
                }

                return;
            }

            var originalBytes = expectedOriginalBytes
                ?? throw new FolderIconOperationException("Windows could not safely read the folder settings.");
            if (!File.ReadAllBytes(path).AsSpan().SequenceEqual(originalBytes))
            {
                throw new FolderIconOperationException(
                    "The folder settings changed while the icon was being applied. Try again.");
            }

            if (originalWasReadOnly)
            {
                File.SetAttributes(path, originalAttributes & ~FileAttributes.ReadOnly);
            }

            try
            {
                File.Replace(temporaryPath, path, null, ignoreMetadataErrors: true);
                File.SetAttributes(
                    path,
                    finalAttributes
                        ?? WithRequiredAttributes(
                            originalAttributes,
                            FileAttributes.Hidden | FileAttributes.System));
            }
            catch (Exception updateException)
            {
                try
                {
                    if (File.Exists(path)
                        && File.ReadAllBytes(path).AsSpan().SequenceEqual(updatedBytes))
                    {
                        ReplaceFileContents(path, originalBytes, originalAttributes);
                    }
                    else if (File.Exists(path)
                        && File.ReadAllBytes(path).AsSpan().SequenceEqual(originalBytes))
                    {
                        File.SetAttributes(path, originalAttributes);
                    }
                }
                catch (Exception rollbackException)
                {
                    throw new FolderIconOperationException(
                        "Windows could not safely update the folder settings. Please check the folder before trying again.",
                        new AggregateException(updateException, rollbackException));
                }

                throw;
            }
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static void ReplaceFileContents(
        string path,
        byte[] bytes,
        FileAttributes attributes)
    {
        var directory = Path.GetDirectoryName(path)
            ?? throw new FolderIconOperationException("Windows could not access this folder.");
        var backupPath = Path.Combine(directory, ".foldericon-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            WriteNewFile(backupPath, bytes);
            if ((attributes & FileAttributes.ReadOnly) != 0)
            {
                File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
            }

            File.Replace(backupPath, path, null, ignoreMetadataErrors: true);
            File.SetAttributes(path, attributes);
        }
        finally
        {
            if (File.Exists(backupPath))
            {
                File.Delete(backupPath);
            }
        }
    }

    private static void WriteNewFile(string path, byte[] bytes)
    {
        using var stream = new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            64 * 1024,
            FileOptions.WriteThrough);
        stream.Write(bytes);
        stream.Flush(flushToDisk: true);
    }

    private static FileAttributes WithRequiredAttributes(
        FileAttributes existingAttributes,
        FileAttributes requiredAttributes)
    {
        return (existingAttributes & ~FileAttributes.Normal) | requiredAttributes;
    }

    private static bool HasShellClassInfoSettings(List<string> lines)
    {
        var section = DesktopIniDocument.FindSection(lines, ".ShellClassInfo");
        if (section is null)
        {
            return false;
        }

        for (var index = section.Value.Start + 1; index < section.Value.End; index++)
        {
            var keyName = DesktopIniDocument.ReadKeyName(lines[index]);
            if (keyName is not null && !IconKeys.Contains(keyName, StringComparer.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private void VerifyCustomization(
        string folder,
        string iconPath,
        string iconFileName,
        byte[] expectedIconBytes,
        byte[] expectedIniBytes)
    {
        var iniPath = Path.Combine(folder, IniFileName);
        if (!Directory.Exists(folder)
            || !File.Exists(iconPath)
            || !File.Exists(iniPath))
        {
            throw new FolderIconOperationException(
                "The folder icon could not be verified on disk. Windows may not have applied the change.");
        }

        var folderAttributes = File.GetAttributes(folder);
        if ((folderAttributes & (FileAttributes.System | FileAttributes.ReadOnly))
            != (FileAttributes.System | FileAttributes.ReadOnly))
        {
            throw new FolderIconOperationException(
                "Windows could not enable folder customization. Check that the folder is writable and try again.");
        }

        var iniAttributes = File.GetAttributes(iniPath);
        if ((iniAttributes & (FileAttributes.Hidden | FileAttributes.System))
            != (FileAttributes.Hidden | FileAttributes.System))
        {
            throw new FolderIconOperationException(
                "Windows could not set the required folder settings. The icon was not reported as applied.");
        }

        if ((File.GetAttributes(iconPath) & FileAttributes.Hidden) == 0)
        {
            throw new FolderIconOperationException(
                "Windows could not save the new icon as a hidden folder file.");
        }

        var actualIniBytes = File.ReadAllBytes(iniPath);
        if (!actualIniBytes.AsSpan().SequenceEqual(expectedIniBytes))
        {
            throw new FolderIconOperationException(
                "The folder settings changed while the icon was being applied. Try again.");
        }

        var actualIconBytes = File.ReadAllBytes(iconPath);
        if (!actualIconBytes.AsSpan().SequenceEqual(expectedIconBytes))
        {
            throw new FolderIconOperationException(
                "The completed icon file could not be verified. The folder icon was not reported as applied.");
        }

        _iconFileBuilder.Validate(actualIconBytes);
        var document = DesktopIniDocument.Read(iniPath, true, actualIniBytes);
        if (!string.Equals(
                DesktopIniDocument.ReadKey(document.Lines, ".ShellClassInfo", "IconResource"),
                iconFileName + ",0",
                StringComparison.OrdinalIgnoreCase)
            || !string.Equals(
                DesktopIniDocument.ReadKey(document.Lines, ".ShellClassInfo", "IconFile"),
                iconFileName,
                StringComparison.OrdinalIgnoreCase)
            || DesktopIniDocument.ReadKey(document.Lines, ".ShellClassInfo", "IconIndex") != "0"
            || !string.Equals(
                DesktopIniDocument.ReadKey(document.Lines, MetadataSection, "Owner"),
                MetadataOwner,
                StringComparison.Ordinal)
            || !string.Equals(
                DesktopIniDocument.ReadKey(document.Lines, MetadataSection, "CurrentIconFile"),
                iconFileName,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new FolderIconOperationException(
                "The folder settings do not point to the completed icon. The change was not reported as successful.");
        }
    }

    private static void VerifyRestoration(
        string folder,
        string iniPath,
        AppState state,
        bool deleteCreatedIni,
        bool expectedFolderSystem,
        bool expectedFolderReadOnly,
        byte[]? expectedIniBytes)
    {
        var folderAttributes = File.GetAttributes(folder);
        if (expectedFolderSystem
                != ((folderAttributes & FileAttributes.System) != 0)
            || expectedFolderReadOnly
                != ((folderAttributes & FileAttributes.ReadOnly) != 0))
        {
            throw new FolderIconOperationException(
                "Windows could not verify the folder's original attributes after restoring its icon.");
        }

        if (deleteCreatedIni)
        {
            if (File.Exists(iniPath))
            {
                throw new FolderIconOperationException(
                    "Windows could not remove the folder settings created for this icon.");
            }

            return;
        }

        if (!File.Exists(iniPath))
        {
            throw new FolderIconOperationException(
                "Windows could not verify the restored folder settings.");
        }

        var actualIniBytes = File.ReadAllBytes(iniPath);
        if (expectedIniBytes is null || !actualIniBytes.AsSpan().SequenceEqual(expectedIniBytes))
        {
            throw new FolderIconOperationException(
                "Windows could not verify the restored folder settings.");
        }

        var document = DesktopIniDocument.Read(iniPath, true, actualIniBytes);
        if (DesktopIniDocument.FindSection(document.Lines, MetadataSection) is not null)
        {
            throw new FolderIconOperationException(
                "Windows could not remove the folder icon's saved settings.");
        }

        if (!state.CreatedIni
            && (File.GetAttributes(iniPath) & (FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReadOnly))
                != ((FileAttributes)state.OriginalIniAttributes
                    & (FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReadOnly)))
        {
            throw new FolderIconOperationException(
                "Windows could not restore the folder settings' original attributes.");
        }
    }

    private static void RollBackDesktopIni(
        string iniPath,
        byte[] expectedCurrentBytes,
        byte[]? originalBytes,
        FileAttributes originalAttributes)
    {
        if (!File.Exists(iniPath)
            || !File.ReadAllBytes(iniPath).AsSpan().SequenceEqual(expectedCurrentBytes))
        {
            throw new FolderIconOperationException(
                "The folder settings changed during verification and were left untouched.");
        }

        if (originalBytes is null)
        {
            File.Delete(iniPath);
            return;
        }

        var currentAttributes = File.GetAttributes(iniPath);
        WriteDocument(
            iniPath,
            expectedCurrentBytes,
            originalBytes,
            currentAttributes,
            originalAttributes);
    }

    private static void RollBackRestoreDesktopIni(
        string iniPath,
        byte[] originalBytes,
        FileAttributes originalAttributes,
        bool deletedIni,
        byte[]? restoredIniBytes)
    {
        if (deletedIni)
        {
            if (File.Exists(iniPath))
            {
                if (!File.ReadAllBytes(iniPath).AsSpan().SequenceEqual(originalBytes))
                {
                    throw new FolderIconOperationException(
                        "The folder settings changed during restore and were left untouched.");
                }

                return;
            }

            WriteDocument(
                iniPath,
                null,
                originalBytes,
                originalAttributes,
                originalAttributes);
            return;
        }

        if (restoredIniBytes is null
            || !File.Exists(iniPath)
            || !File.ReadAllBytes(iniPath).AsSpan().SequenceEqual(restoredIniBytes))
        {
            throw new FolderIconOperationException(
                "The folder settings changed during restore and were left untouched.");
        }

        WriteDocument(
            iniPath,
            restoredIniBytes,
            originalBytes,
            File.GetAttributes(iniPath),
            originalAttributes);
    }

    private static string NormalizeFolderPath(string folderPath)
    {
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(folderPath));
    }

    private static IDisposable AcquireFolderLock(string folder)
    {
        var normalized = folder.ToUpperInvariant();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
        return new FolderMutex("Local\\FolderIconChanger-" + hash);
    }

    private sealed class FolderMutex : IDisposable
    {
        private readonly Mutex _mutex;
        private bool _ownsMutex;

        public FolderMutex(string name)
        {
            _mutex = new Mutex(initiallyOwned: false, name);
            try
            {
                _mutex.WaitOne();
                _ownsMutex = true;
            }
            catch (AbandonedMutexException)
            {
                _ownsMutex = true;
            }
        }

        public void Dispose()
        {
            if (_ownsMutex)
            {
                _mutex.ReleaseMutex();
                _ownsMutex = false;
            }

            _mutex.Dispose();
        }
    }

    private sealed record AppState(
        string CurrentIconFile,
        bool OriginalFolderSystem,
        bool OriginalFolderReadOnly,
        bool CreatedIni,
        bool CreatedShellClassInfo,
        bool AddedShellClassInfoSeparator,
        int OriginalIniAttributes,
        Dictionary<string, List<string>> OriginalIconLines);
}
