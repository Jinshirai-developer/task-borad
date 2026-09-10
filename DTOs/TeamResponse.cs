namespace TaskApi.DTOs;

public class TeamResponse
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int OwnerUserProfileId { get; set; }
    public string Role { get; set; } = "member";
    public int MemberCount { get; set; }
    public int TotalTasks { get; set; }
    public int TodoCount { get; set; }
    public int DoingCount { get; set; }
    public int DoneCount { get; set; }
    public int OverdueCount { get; set; }
}

public sealed class TeamDetailResponse : TeamResponse
{
    public List<TeamMemberResponse> Members { get; set; } = [];
}

public sealed record TeamMemberResponse(int UserProfileId, string DisplayName, string Role);
public sealed record TeamSummaryResponse(int Total, int Todo, int Doing, int Done, int Overdue);
public sealed record TeamCreatedResponse(TeamDetailResponse Team, string InviteCode, DateTime ExpiresAt);
public sealed record TeamInviteResponse(string InviteCode, DateTime ExpiresAt);
