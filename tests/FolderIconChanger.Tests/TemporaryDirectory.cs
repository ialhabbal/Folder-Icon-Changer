namespace FolderIconChanger.Tests;

internal sealed class TemporaryDirectory : IDisposable
{
    public TemporaryDirectory()
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "Folder Icon Changer Tests " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public void Dispose()
    {
        if (Directory.Exists(Path))
        {
            foreach (var file in Directory.EnumerateFiles(Path, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }

            foreach (var directory in Directory
                         .EnumerateDirectories(Path, "*", SearchOption.AllDirectories)
                         .OrderByDescending(static directory => directory.Length))
            {
                var attributes = File.GetAttributes(directory);
                File.SetAttributes(
                    directory,
                    attributes & ~(FileAttributes.ReadOnly | FileAttributes.System));
            }

            var rootAttributes = File.GetAttributes(Path);
            File.SetAttributes(
                Path,
                rootAttributes & ~(FileAttributes.ReadOnly | FileAttributes.System));
            Directory.Delete(Path, recursive: true);
        }
    }
}

internal sealed class TemporaryDirectoryOnProjectDrive : IDisposable
{
    public TemporaryDirectoryOnProjectDrive()
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetPathRoot(Environment.CurrentDirectory)!,
            "Folder Icon Changer Test " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public void Dispose()
    {
        if (Directory.Exists(Path))
        {
            foreach (var file in Directory.EnumerateFiles(Path, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }

            foreach (var directory in Directory
                         .EnumerateDirectories(Path, "*", SearchOption.AllDirectories)
                         .OrderByDescending(static directory => directory.Length))
            {
                var attributes = File.GetAttributes(directory);
                File.SetAttributes(
                    directory,
                    attributes & ~(FileAttributes.ReadOnly | FileAttributes.System));
            }

            var rootAttributes = File.GetAttributes(Path);
            File.SetAttributes(
                Path,
                rootAttributes & ~(FileAttributes.ReadOnly | FileAttributes.System));
            Directory.Delete(Path, recursive: true);
        }
    }
}
