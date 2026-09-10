using System.ComponentModel.DataAnnotations;

namespace TaskApi.DTOs;

public class ConsentRequest
{
    public bool AcceptTerms { get; set; }
    [Required, MaxLength(30)] public string TermsVersion { get; set; } = string.Empty;
    [Required, MaxLength(30)] public string PrivacyVersion { get; set; } = string.Empty;
}

public sealed class GoogleRegistrationRequest : ConsentRequest
{
    [Required, MinLength(3), MaxLength(100), RegularExpression("^[a-zA-Z0-9_-]+$")]
    public string UserKey { get; set; } = string.Empty;
    [MaxLength(100)] public string? DisplayName { get; set; }
}
