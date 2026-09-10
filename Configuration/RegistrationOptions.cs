namespace TaskApi.Configuration;

public sealed class RegistrationOptions
{
    public const string SectionName = "Registration";

    public bool Enabled { get; set; } = true;

    public int MaxUsers { get; set; } = 100;
}
