using System.ComponentModel.DataAnnotations;

namespace TaskApi.DTOs;

public class LoginUserRequest
{
    [Required]
    [MaxLength(100)]
    public string UserKey { get; set; } = string.Empty;

    [Required]
    [MaxLength(100)]
    public string Password { get; set; } = string.Empty;
}
