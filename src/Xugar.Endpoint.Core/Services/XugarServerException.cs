namespace Xugar.Endpoint.Core.Services;

public enum ServerFailureKind
{
    Retryable,
    Authentication,
    NonRetryable,
    MalformedResponse
}

public sealed class XugarServerException(
    ServerFailureKind kind,
    string message,
    int? statusCode = null,
    Exception? innerException = null) : Exception(message, innerException)
{
    public ServerFailureKind Kind { get; } = kind;

    public int? StatusCode { get; } = statusCode;
}
