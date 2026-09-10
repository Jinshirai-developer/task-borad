namespace TaskApi.Models;

// No card details, email, Stripe payloads or secret keys are stored here.
public sealed class TeamBilling
{
    // The account is the billing identity. The historical TeamId is ONLY a
    // Stripe metadata snapshot for pre-account contracts, never an entitlement.
    public int UserProfileId { get; set; }
    public int TeamId { get; set; }
    public string MetadataScope { get; set; } = "account";
    public string Status { get; set; } = "free";
    public string? AttemptId { get; set; }
    public string? PriceId { get; set; }
    public int MonthlyYen { get; set; }
    public string? ReturnOrigin { get; set; }
    public DateTime? AttemptStartedAt { get; set; }
    public string? SessionId { get; set; }
    public string? SubscriptionId { get; set; }
    public DateTime? PaidThrough { get; set; }
    public bool CancelAtPeriodEnd { get; set; }
    public DateTime? LastSyncedAt { get; set; }
    public string? OperationToken { get; set; }
    public DateTime? OperationUntil { get; set; }
    public bool HasPro(DateTime now) => PaidThrough > now && Status is "active" or "past_due";
    public bool HasContract => AttemptId != null && Status is not ("canceled" or "incomplete_expired" or "expired");
}

public sealed class BillingEventReceipt
{
    public string Id { get; set; } = "";
    public int UserProfileId { get; set; }
    public int TeamId { get; set; }
    public DateTime ProcessedAt { get; set; } = DateTime.UtcNow;
}
