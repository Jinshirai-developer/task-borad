using System.ComponentModel.DataAnnotations;

namespace TaskApi.DTOs;

public class RegisterUserRequest
{
    [Required]
    [MinLength(3)]
    [MaxLength(100)]
    [RegularExpression("^[A-Za-z0-9_-]+$", ErrorMessage = "ユーザーIDは半角英数字、ハイフン、アンダースコアで入力してください。")]
    public string UserKey { get; set; } = string.Empty;

    [MaxLength(100)]
    public string? DisplayName { get; set; }

    [Required]
    [MinLength(12)]
    [MaxLength(100)]
    public string Password { get; set; } = string.Empty;

    [Required, EmailAddress, MaxLength(254)]
    public string Email { get; set; } = string.Empty;

    public bool AcceptTerms { get; set; }

    [Required, MaxLength(30)]
    public string TermsVersion { get; set; } = string.Empty;

    [Required, MaxLength(30)]
    public string PrivacyVersion { get; set; } = string.Empty;
}
