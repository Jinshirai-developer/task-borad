namespace TaskApi.DTOs;

public class UserProfileResponse
{
    public int Id { get; set; }

    public string UserKey { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;
}
