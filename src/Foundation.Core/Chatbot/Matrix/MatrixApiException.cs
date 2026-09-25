using System.Net;

namespace Foundation.Core.Chatbot.Matrix;

public sealed class MatrixApiException(HttpStatusCode statusCode, string? matrixErrorCode, string message)
    : Exception(message)
{
    public HttpStatusCode StatusCode { get; } = statusCode;
    public string? MatrixErrorCode { get; } = matrixErrorCode;
}
