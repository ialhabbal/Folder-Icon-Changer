namespace FolderIconChanger.Core;

public sealed class ImageProcessingException : Exception
{
    public ImageProcessingException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
