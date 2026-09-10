using Microsoft.Extensions.Options;
using Stripe;
using Stripe.Checkout;
using TaskApi.Configuration;
using TaskApi.Models;

namespace TaskApi.Services;

public sealed record BillingSnapshot(string SessionId, string Status, string? SubscriptionId,
    DateTime? PaidThrough, bool CancelAtPeriodEnd, string? CheckoutUrl = null);

public interface IStripeTestGateway
{
    Task ValidatePlanAsync(string priceId, int monthlyYen, CancellationToken cancellationToken);
    Task<BillingSnapshot> CheckoutAsync(TeamBilling billing, CancellationToken cancellationToken);
    Task<BillingSnapshot> RefreshAsync(TeamBilling billing, CancellationToken cancellationToken);
    Task<BillingSnapshot> ChangeAsync(TeamBilling billing, string action, CancellationToken cancellationToken);
}

public sealed class StripeTestGateway(IOptions<BillingOptions> settings, IStripeClient? clientOverride = null) : IStripeTestGateway
{
    private IStripeClient Client()
    {
        if (!settings.Value.IsConfigured) throw new TeamOperationException("billing_unavailable", "Stripeテスト接続はまだ設定されていません。", 503);
        return clientOverride ?? new StripeClient(settings.Value.SecretKey);
    }
    private static void RequireTest(bool live)
    {
        if (live) throw new TeamOperationException("billing_live_rejected", "本番モードの決済・契約は処理できません。", 400);
    }
    private static void Match(bool valid)
    {
        if (!valid) throw new TeamOperationException("billing_mismatch", "契約とアカウントの対応を確認できません。管理者に設定確認を依頼してください。", 409);
    }
    private static void Metadata(Dictionary<string,string>? metadata, TeamBilling billing) => Match(metadata != null
        && metadata.GetValueOrDefault("taskboard_attempt") == billing.AttemptId
        && (billing.MetadataScope == "team"
            ? metadata.GetValueOrDefault("taskboard_team") == billing.TeamId.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : billing.MetadataScope == "account" && metadata.GetValueOrDefault("taskboard_account") == billing.UserProfileId.ToString(System.Globalization.CultureInfo.InvariantCulture)));
    private static void CheckPrice(Price price, TeamBilling billing, bool creating = false)
    {
        RequireTest(price.Livemode);
        Match(price.Id == billing.PriceId && (!creating || price.Active) && price.Currency == "jpy" && price.UnitAmount == billing.MonthlyYen
            && price.Recurring?.Interval == "month" && price.Recurring.IntervalCount == 1 && price.Type == "recurring");
    }
    public async Task ValidatePlanAsync(string priceId, int monthlyYen, CancellationToken cancellationToken)
    {
        var price = await new PriceService(Client()).GetAsync(priceId, cancellationToken: cancellationToken);
        CheckPrice(price, new TeamBilling { PriceId = priceId, MonthlyYen = monthlyYen }, creating: true);
    }
    public async Task<BillingSnapshot> CheckoutAsync(TeamBilling billing, CancellationToken cancellationToken)
    {
        if (billing.SessionId != null) return await RefreshAsync(billing, cancellationToken);
        // Stripe keeps idempotency keys for at least 24 hours; never blindly create again beyond it.
        if (billing.AttemptStartedAt < DateTime.UtcNow.AddHours(-23))
            throw new TeamOperationException("billing_recovery_required", "決済開始の応答を確認できないまま時間が経過しました。重複契約を防ぐため、Stripeのテスト環境で確認が必要です。", 409);
        var client = Client();
        // Recover legacy attempts using their EXACT original parameters.
        // Migration never rewrites existing Stripe subscriptions or metadata.
        var metadata = new Dictionary<string,string> { [billing.MetadataScope == "team" ? "taskboard_team" : "taskboard_account"] =
            (billing.MetadataScope == "team" ? billing.TeamId : billing.UserProfileId).ToString(System.Globalization.CultureInfo.InvariantCulture), ["taskboard_attempt"] = billing.AttemptId! };
        var returnScope = billing.MetadataScope == "team" ? $"team={billing.TeamId}" : "account=1";
        var session = await new SessionService(client).CreateAsync(new SessionCreateOptions
        {
            Mode = "subscription", PaymentMethodTypes = ["card"], Locale = "ja",
            LineItems = [new SessionLineItemOptions { Price = billing.PriceId, Quantity = 1 }],
            CustomerEmail = "team-pro-test@example.com", ClientReferenceId = billing.AttemptId,
            Metadata = metadata, SubscriptionData = new SessionSubscriptionDataOptions { Metadata = metadata },
            SuccessUrl = $"{billing.ReturnOrigin}/index.html?billing=return&{returnScope}",
            CancelUrl = $"{billing.ReturnOrigin}/index.html?billing=cancel&{returnScope}",
            ExpiresAt = billing.AttemptStartedAt!.Value.AddHours(1),
            CustomText = new SessionCustomTextOptions { Submit = new SessionCustomTextSubmitOptions { Message = "動作確認専用です。実際の請求は発生しません。実カード・実個人情報を入力しないでください。" } }
        }, new RequestOptions { IdempotencyKey = $"taskboard-checkout-{billing.AttemptId}" }, cancellationToken);
        return await DescribeAsync(client, session, billing, cancellationToken);
    }
    public async Task<BillingSnapshot> RefreshAsync(TeamBilling billing, CancellationToken cancellationToken)
    {
        if (billing.SessionId == null) throw new TeamOperationException("billing_checkout_pending", "決済開始を確認できていません。「テスト決済を試す」で同じ処理を再確認してください。", 409);
        var client = Client();
        var session = await new SessionService(client).GetAsync(billing.SessionId, cancellationToken: cancellationToken);
        return await DescribeAsync(client, session, billing, cancellationToken);
    }
    public async Task<BillingSnapshot> ChangeAsync(TeamBilling billing, string action, CancellationToken cancellationToken)
    {
        if (billing.SessionId == null && action == "abandon")
        {
            // Recover the original idempotent operation before expiring it, including lost responses.
            var recovered = await CheckoutAsync(billing, cancellationToken);
            billing.SessionId = recovered.SessionId;
        }
        if (billing.SessionId == null) throw new TeamOperationException("billing_checkout_pending", "先に決済開始の結果を再確認してください。", 409);
        var client = Client();
        // Retrieve and verify before every mutation; no caller-supplied customer/subscription IDs.
        var session = await new SessionService(client).GetAsync(billing.SessionId, cancellationToken: cancellationToken);
        var current = await DescribeAsync(client, session, billing, cancellationToken);
        if (action == "abandon" && session.Status == "open")
            await new SessionService(client).ExpireAsync(session.Id, cancellationToken: cancellationToken);
        else if (action == "abandon")
            throw new TeamOperationException("billing_already_completed", "決済の状態が変わりました。最新の状態を確認してください。", 409);
        else if (current.SubscriptionId == null)
            throw new TeamOperationException("billing_no_subscription", "変更できる契約がありません。", 409);
        else if (current.Status is not ("canceled" or "incomplete_expired"))
        {
            var service = new SubscriptionService(client);
            if (action == "end_now")
                await service.CancelAsync(current.SubscriptionId, new SubscriptionCancelOptions { InvoiceNow = false, Prorate = false }, cancellationToken: cancellationToken);
            else
                await service.UpdateAsync(current.SubscriptionId, new SubscriptionUpdateOptions { CancelAtPeriodEnd = action == "cancel" },
                    new RequestOptions { IdempotencyKey = $"taskboard-{action}-{billing.OperationToken}" }, cancellationToken);
        }
        return await RefreshAsync(billing, cancellationToken);
    }
    private static async Task<BillingSnapshot> DescribeAsync(IStripeClient client, Session session, TeamBilling billing, CancellationToken cancellationToken)
    {
        RequireTest(session.Livemode); Metadata(session.Metadata, billing);
        Match(session.Mode == "subscription" && session.ClientReferenceId == billing.AttemptId
            && (billing.SessionId == null || billing.SessionId == session.Id) && session.Id.StartsWith("cs_test_", StringComparison.Ordinal));
        if (session.SubscriptionId == null)
        {
            var url = session.Status == "open" ? session.Url : null;
            if (url != null) Match(Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == "https" && uri.Host == "checkout.stripe.com" && uri.UserInfo == "");
            return new(session.Id, session.Status == "expired" ? "expired" : "checkout", null, null, false, url);
        }
        var subscription = await new SubscriptionService(client).GetAsync(session.SubscriptionId,
            new SubscriptionGetOptions { Expand = ["latest_invoice"] }, cancellationToken: cancellationToken);
        RequireTest(subscription.Livemode); Metadata(subscription.Metadata, billing);
        Match(billing.SubscriptionId == null || billing.SubscriptionId == subscription.Id);
        Match(subscription.Items.Data.Count == 1);
        var item = subscription.Items.Data[0];
        CheckPrice(item.Price, billing); Match(item.Quantity == 1);
        var invoice = subscription.LatestInvoice;
        if (invoice != null) RequireTest(invoice.Livemode);
        DateTime? paidThrough = invoice?.Status == "paid" && invoice.AmountPaid == billing.MonthlyYen && invoice.Currency == "jpy"
            && subscription.Status == "active" ? item.CurrentPeriodEnd : null;
        return new(session.Id, subscription.Status, subscription.Id, paidThrough, subscription.CancelAtPeriodEnd);
    }
}
