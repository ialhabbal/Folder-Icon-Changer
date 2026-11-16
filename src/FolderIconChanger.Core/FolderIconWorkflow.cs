namespace FolderIconChanger.Core;

public sealed class FolderIconWorkflow
{
    private readonly FolderIconService _folderIconService;

    public FolderIconWorkflow(FolderIconService? folderIconService = null)
    {
        _folderIconService = folderIconService ?? new FolderIconService();
    }

    public async Task<FolderIconResult> ApplyAsync(
        string folderPath,
        string imagePath,
        Action<FolderIconResult>? onVerifiedSuccess = null)
    {
        var result = await _folderIconService.ApplyAsync(folderPath, imagePath);
        onVerifiedSuccess?.Invoke(result);
        return result;
    }
}
