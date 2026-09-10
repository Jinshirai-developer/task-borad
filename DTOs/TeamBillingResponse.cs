namespace TaskApi.DTOs;

public sealed record TeamBillingResponse(int TeamId, string Plan, string Status, int MemberCount, int? MemberLimit,
    bool CanJoin, bool IsOwner, bool CheckoutAvailable, int MonthlyYen, bool CancelAtPeriodEnd,
    DateTime? PaidThrough, DateTime? LastSyncedAt, bool HasContract)
{
    public bool TestOnly => true;
    public string Scope => "account";
    public int BillingUserProfileId { get; init; }
    public string BillingDisplayName { get; init; } = "";
    public int OwnedTeamCount { get; init; }
}
public sealed record BillingCheckoutResponse(string? Url, TeamBillingResponse Billing);
