namespace TaskApi.Services;

public sealed class AccountOperationException(int statusCode, string message)
    : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}
