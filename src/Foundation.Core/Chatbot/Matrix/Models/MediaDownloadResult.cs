namespace Foundation.Core.Chatbot.Matrix.Models;

public sealed class MediaDownloadResult
{
    public required byte[] Bytes { get; init; }
    public string? ContentType { get; init; }
    public string? FileName { get; init; }
}
