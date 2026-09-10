using System.ComponentModel.DataAnnotations;

namespace TaskApi.DTOs;

public class EmailRequest
{
    [Required, EmailAddress, MaxLength(254)]
    public string Email { get; set; } = string.Empty;
}

public class CompleteRegistrationRequest : EmailRequest
{
    [Required, MaxLength(100)]
    public string Password { get; set; } = string.Empty;
    public bool AcceptTerms { get; set; }
    [Required, MaxLength(30)]
    public string TermsVersion { get; set; } = string.Empty;
    [Required, MaxLength(30)]
    public string PrivacyVersion { get; set; } = string.Empty;
}

public class EmailTokenRequest
{
    [Range(1, int.MaxValue)]
    public int UserId { get; set; }
    [Required, MaxLength(4096)]
    public string Token { get; set; } = string.Empty;
}

public class ResetPasswordRequest : EmailTokenRequest
{
    [Required, MinLength(12), MaxLength(100)]
    public string Password { get; set; } = string.Empty;
}
