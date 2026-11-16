namespace FolderIconChanger.Core;

public sealed class FolderIconOperationException : Exception
{
    public FolderIconOperationException(string message)
        : base(message)
    {
    }

    public FolderIconOperationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
