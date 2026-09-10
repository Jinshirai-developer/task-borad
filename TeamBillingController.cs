using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Stripe;
using TaskApi.Authentication;
using TaskApi.Configuration;
using TaskApi.Data;
using TaskApi.Services;

namespace TaskApi.Controllers;

[ApiController]
[Route("api/teams/{teamId:int}/billing")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class TeamBillingController(TeamBillingService billing, AppDbContext database, IOptions<BillingOptions> settings) : ControllerBase
{
    [HttpGet] public IActionResult Get(int teamId)
    {
        try { return Ok(billing.Get(RequireAuthenticationAttribute.GetAuthenticatedUser(HttpContext).Id, teamId)); }
        catch (TeamOperationException error) { return Failure(error); }
    }
    [HttpGet("/api/user/billing")] public IActionResult GetAccount()
    {
        try { return Ok(billing.GetAccount(RequireAuthenticationAttribute.GetAuthenticatedUser(HttpContext).Id)); }
        catch (TeamOperationException error) { return Failure(error); }
    }
    [HttpPost("/api/user/billing/{operation:regex(^(checkout|sync|cancel|resume|end_now|abandon)$)}")]
    public async Task<IActionResult> ChangeAccount(string operation, CancellationToken cancellationToken)
    {
        try { return Ok(await billing.ExecuteAccountAsync(RequireAuthenticationAttribute.GetAuthenticatedUser(HttpContext).Id, operation, cancellationToken)); }
        catch (TeamOperationException error) { return Failure(error); }
        catch (Exception error) when (error is StripeException or HttpRequestException or OperationCanceledException or DbUpdateException)
        { return StatusCode(503, new { code = "billing_pending", message = "Stripeの結果を確認できませんでした。重複購入せず、時間をおいて最新の状態を確認してください。" }); }
    }
    [HttpPost("{operation:regex(^(checkout|sync|cancel|resume|end_now|abandon)$)}")]
    public async Task<IActionResult> Change(int teamId, string operation, CancellationToken cancellationToken)
    {
        try { return Ok(await billing.ExecuteAsync(RequireAuthenticationAttribute.GetAuthenticatedUser(HttpContext).Id, teamId, operation, cancellationToken)); }
        catch (TeamOperationException error) { return Failure(error); }
        catch (Exception error) when (error is StripeException or HttpRequestException or OperationCanceledException or DbUpdateException)
        { return StatusCode(503, new { code = "billing_pending", message = "Stripeの結果を確認できませんでした。重複作成せず同じ処理を再確認します。時間をおいて最新の状態を確認してください。" }); }
    }
    private IActionResult Failure(TeamOperationException error) => StatusCode(error.StatusCode, new { code = error.Code, message = error.Message });

    [HttpPost("/api/billing/stripe-webhook"), AllowAnonymous, StripeWebhook, RequestSizeLimit(128 * 1024)]
    public async Task<IActionResult> Webhook(CancellationToken cancellationToken)
    {
        if (!settings.Value.IsConfigured) return StatusCode(503);
        using var reader = new StreamReader(Request.Body);
        var payload = await reader.ReadToEndAsync(cancellationToken);
        Event notification;
        try { notification = EventUtility.ConstructEvent(payload, Request.Headers["Stripe-Signature"].ToString(), settings.Value.WebhookSecret); }
        catch (Exception error) when (error is StripeException or JsonException or Newtonsoft.Json.JsonException or ArgumentException) { return BadRequest(); }
        if (notification.Livemode || string.IsNullOrEmpty(notification.Id) || notification.Id.Length > 255) return BadRequest();
        if (notification.Type is not ("checkout.session.completed" or "checkout.session.expired" or "customer.subscription.created" or "customer.subscription.updated"
            or "customer.subscription.deleted" or "invoice.paid" or "invoice.payment_failed" or "invoice.payment_action_required")) return Ok();
        if (await database.BillingEventReceipts.AnyAsync(item => item.Id == notification.Id, cancellationToken)) return Ok();
        using var document = JsonDocument.Parse(payload);
        var data = document.RootElement.GetProperty("data").GetProperty("object");
        var objectId = data.GetProperty("id").GetString();
        string? attempt = data.TryGetProperty("metadata", out var metadata) && metadata.ValueKind == JsonValueKind.Object
            && metadata.TryGetProperty("taskboard_attempt", out var id) ? id.GetString() : null;
        string? subscription = notification.Type.StartsWith("customer.subscription.", StringComparison.Ordinal) ? objectId : null;
        if (subscription == null && data.TryGetProperty("subscription", out var legacy) && legacy.ValueKind == JsonValueKind.String) subscription = legacy.GetString();
        if (subscription == null && data.TryGetProperty("parent", out var parent) && parent.ValueKind == JsonValueKind.Object
            && parent.TryGetProperty("subscription_details", out var details) && details.ValueKind == JsonValueKind.Object
            && details.TryGetProperty("subscription", out var nested) && nested.ValueKind == JsonValueKind.String) subscription = nested.GetString();
        var row = await database.TeamBillings.AsNoTracking().SingleOrDefaultAsync(item => (attempt != null && item.AttemptId == attempt)
            || (subscription != null && item.SubscriptionId == subscription) || (objectId != null && item.SessionId == objectId), cancellationToken);
        if (row == null) return Ok(); // Another app's test event, not a new entitlement.
        try { await billing.ReconcileAsync(row.UserProfileId, notification.Id, cancellationToken); return Ok(); }
        catch (Exception error) when (error is TeamOperationException or StripeException or HttpRequestException or OperationCanceledException or DbUpdateException)
        { return StatusCode(503); } // Retry safely; receipt is committed only after successful authoritative reconciliation.
    }
}
