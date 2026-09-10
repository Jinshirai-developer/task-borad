using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TaskApi.Configuration;
using TaskApi.Data;
using TaskApi.DTOs;
using TaskApi.Models;

namespace TaskApi.Services;

// Team endpoints remain compatible; durable operations and entitlements are account-scoped.
public sealed class TeamBillingService(AppDbContext context, TeamService teams, IStripeTestGateway stripe,
    IOptions<BillingOptions> settings, IOptions<Configuration.AuthenticationOptions> authentication)
{
    public TeamBillingResponse Get(int userId, int teamId)
    {
        var team = teams.RequireMember(userId, teamId);
        return Response(team.OwnerUserProfileId, team, userId);
    }
    public TeamBillingResponse GetAccount(int userId) => Response(userId, null, userId);
    private TeamBillingResponse Response(int accountId, Team? team, int viewerId)
    {
        var owner = context.UserProfiles.AsNoTracking().SingleOrDefault(item => item.Id == accountId)
            ?? throw new TeamOperationException("account_not_found", "アカウントが見つかりません。再ログインしてください。", 401);
        var billing = context.TeamBillings.AsNoTracking().SingleOrDefault(item => item.UserProfileId == accountId);
        var count = team == null ? 0 : context.TeamMembers.Count(item => item.TeamId == team.Id);
        var pro = billing?.HasPro(DateTime.UtcNow) == true;
        return new(team?.Id ?? 0, pro ? "pro" : "free", billing?.Status ?? "free", count, pro ? null : TeamService.MaximumMembers,
            pro || count < TeamService.MaximumMembers, accountId == viewerId, settings.Value.IsConfigured,
            billing?.MonthlyYen > 0 && billing.HasContract ? billing.MonthlyYen : settings.Value.MonthlyYen,
            billing?.CancelAtPeriodEnd ?? false, billing?.PaidThrough, billing?.LastSyncedAt, billing?.HasContract ?? false)
        {
            BillingUserProfileId = accountId, BillingDisplayName = owner.DisplayName,
            OwnedTeamCount = context.Teams.Count(item => item.OwnerUserProfileId == accountId)
        };
    }
    public Task<BillingCheckoutResponse> ExecuteAsync(int userId, int teamId, string action, CancellationToken cancellationToken = default)
        => RunAsync(userId, userId, teamId, action, null, cancellationToken);
    public Task<BillingCheckoutResponse> ExecuteAccountAsync(int userId, string action, CancellationToken cancellationToken = default)
        => RunAsync(userId, userId, null, action, null, cancellationToken);
    public Task<BillingCheckoutResponse> ReconcileAsync(int accountId, string? eventId = null, CancellationToken cancellationToken = default)
        => RunAsync(null, accountId, null, "sync", eventId, cancellationToken);

    private async Task<BillingCheckoutResponse> RunAsync(int? viewerId, int accountId, int? teamId, string action, string? eventId, CancellationToken cancellationToken)
    {
        if (action is not ("checkout" or "sync" or "cancel" or "resume" or "end_now" or "abandon"))
            throw new TeamOperationException("billing_action", "対応していない操作です。", 400);
        if (viewerId.HasValue)
        {
            var before = teamId.HasValue ? Get(viewerId.Value, teamId.Value) : GetAccount(viewerId.Value);
            if (!before.IsOwner) throw new TeamOperationException("owner_required", "契約の操作は所有者本人のアカウントで行ってください。", 403);
            if (!before.CheckoutAvailable) throw new TeamOperationException("billing_unavailable", "Stripeテスト接続は未設定です。", 503);
            if (action == "checkout" && !before.HasContract)
                await stripe.ValidatePlanAsync(settings.Value.PriceId, settings.Value.MonthlyYen, cancellationToken);
        }
        TeamBilling billing;
        using (var scope = WorkspaceWriteScope.Begin(context))
        {
            context.ChangeTracker.Clear();
            if (!context.UserProfiles.Any(item => item.Id == accountId))
                throw new TeamOperationException("account_not_found", "アカウントが見つかりません。", 404);
            if (teamId.HasValue && teams.RequireMember(viewerId!.Value, teamId.Value).OwnerUserProfileId != accountId)
                throw new TeamOperationException("owner_required", "チームの所有者が変わりました。最新の状態を確認してください。", 403);
            if (!settings.Value.IsConfigured) throw new TeamOperationException("billing_unavailable", "Stripeテスト接続は未設定です。人数制限と既存データの保護は有効です。", 503);
            billing = context.TeamBillings.SingleOrDefault(item => item.UserProfileId == accountId) ?? new TeamBilling { UserProfileId = accountId };
            if (eventId != null && context.BillingEventReceipts.Any(item => item.Id == eventId))
                return new(null, Response(accountId, null, viewerId ?? 0));
            if (billing.OperationUntil > DateTime.UtcNow) throw new TeamOperationException("billing_busy", "別の決済操作を確認中です。少し待って最新状態を確認してください。", 409);
            if (action == "checkout" && !billing.HasContract)
            {
                billing.AttemptId = Guid.NewGuid().ToString();
                billing.AttemptStartedAt = new DateTime(DateTime.UtcNow.Ticks / TimeSpan.TicksPerSecond * TimeSpan.TicksPerSecond, DateTimeKind.Utc);
                billing.TeamId = teamId ?? 0; billing.MetadataScope = "account";
                billing.PriceId = settings.Value.PriceId; billing.MonthlyYen = settings.Value.MonthlyYen;
                billing.ReturnOrigin = authentication.Value.PublicBaseUrl.TrimEnd('/');
                billing.Status = "checkout"; billing.SessionId = null; billing.SubscriptionId = null;
                billing.PaidThrough = null; billing.CancelAtPeriodEnd = false;
            }
            else if (billing.AttemptId == null)
                return new(null, Response(accountId, teamId.HasValue ? teams.RequireMember(viewerId!.Value, teamId.Value) : null, viewerId ?? 0));
            if (context.Entry(billing).State == EntityState.Detached) context.TeamBillings.Add(billing);
            billing.OperationToken = Guid.NewGuid().ToString(); billing.OperationUntil = DateTime.UtcNow.AddMinutes(2);
            context.SaveChanges(); scope.Commit();
        }
        var operationToken = billing.OperationToken;
        try
        {
            // One account-wide lease, even if two different teams request checkout.
            // Never await while holding the thread-affine lock/DB transaction.
            var snapshot = action == "checkout" ? await stripe.CheckoutAsync(billing, cancellationToken)
                : action == "sync" ? await stripe.RefreshAsync(billing, cancellationToken)
                : await stripe.ChangeAsync(billing, action, cancellationToken);
            using var scope = WorkspaceWriteScope.Begin(context);
            context.ChangeTracker.Clear();
            var current = context.TeamBillings.Single(item => item.UserProfileId == accountId);
            if (current.OperationToken != operationToken || current.OperationUntil <= DateTime.UtcNow)
                throw new TeamOperationException("billing_conflict", "状態確認が競合しました。最新の状態を確認してください。", 409);
            current.SessionId = snapshot.SessionId; current.SubscriptionId = snapshot.SubscriptionId; current.Status = snapshot.Status;
            // Failed renewals cannot erase an already-paid interval.
            if (snapshot.PaidThrough > current.PaidThrough || (snapshot.PaidThrough != null && current.PaidThrough == null)) current.PaidThrough = snapshot.PaidThrough;
            if (snapshot.Status is "canceled" or "incomplete_expired" or "expired") current.PaidThrough = null;
            current.CancelAtPeriodEnd = snapshot.CancelAtPeriodEnd; current.LastSyncedAt = DateTime.UtcNow;
            current.OperationToken = null; current.OperationUntil = null;
            if (eventId != null && !context.BillingEventReceipts.Any(item => item.Id == eventId))
                context.BillingEventReceipts.Add(new BillingEventReceipt { Id = eventId, UserProfileId = accountId, TeamId = current.TeamId });
            context.SaveChanges(); scope.Commit();
            Team? team = teamId.HasValue ? teams.RequireMember(viewerId!.Value, teamId.Value) : null;
            if (team != null && team.OwnerUserProfileId != accountId)
                throw new TeamOperationException("billing_scope_changed", "チームの所有者が変わりました。契約は購入したアカウントに保持されています。設定 → アカウントから確認してください。", 409);
            return new(action == "checkout" ? snapshot.CheckoutUrl : null, Response(accountId, team, viewerId ?? 0));
        }
        finally
        {
            using var scope = WorkspaceWriteScope.Begin(context);
            context.ChangeTracker.Clear();
            var current = context.TeamBillings.SingleOrDefault(item => item.UserProfileId == accountId);
            if (current?.OperationToken == operationToken) { current.OperationToken = null; current.OperationUntil = null; context.SaveChanges(); }
            scope.Commit();
        }
    }
}
