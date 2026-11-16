namespace FolderIconChanger.Core;

public interface IFolderIconChangeNotifier
{
    void NotifyFolderChanged(string folderPath);
}
