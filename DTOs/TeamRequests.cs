using System.ComponentModel.DataAnnotations;

namespace TaskApi.DTOs;

public sealed class CreateTeamRequest
{
    [Required, MaxLength(100)]
    public string Name { get; set; } = string.Empty;
}

public sealed class JoinTeamRequest
{
    [Required, StringLength(100, MinimumLength = 20)]
    public string InviteCode { get; set; } = string.Empty;
}

public sealed class TransferTeamOwnerRequest
{
    [Range(1, int.MaxValue)]
    public int UserProfileId { get; set; }
}
