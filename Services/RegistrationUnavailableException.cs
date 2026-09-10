namespace TaskApi.Services;

public sealed class RegistrationUnavailableException : InvalidOperationException
{
    public RegistrationUnavailableException(string message)
        : base(message)
    {
    }
}
