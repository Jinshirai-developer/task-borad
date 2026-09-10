using System.ComponentModel.DataAnnotations;

namespace TaskApi.DTOs;

public class UpdateUserProfileRequest
{
    [Required]
    [MaxLength(100)]
    public string DisplayName { get; set; } = string.Empty;
}
