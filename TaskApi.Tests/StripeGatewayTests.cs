using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Stripe;
using TaskApi.Models;
using TaskApi.Services;

namespace TaskApi.Tests;

public sealed class StripeGatewayTests
{
    private sealed class Handler(Func<HttpRequestMessage,string> reply) : HttpMessageHandler
    {
        public readonly List<(string? Url,string? Key,string Body)> Requests=[];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken)
        {
            Requests.Add((request.RequestUri?.PathAndQuery,request.Headers.TryGetValues("Idempotency-Key",out var keys)?keys.Single():null,request.Content==null?"":await request.Content.ReadAsStringAsync(cancellationToken)));
            return new(HttpStatusCode.OK){Content=new StringContent(reply(request),Encoding.UTF8,"application/json")};
        }
    }
    private static TeamBilling Record()=>new(){UserProfileId=7,TeamId=1,MetadataScope="team",AttemptId="attempt-1",AttemptStartedAt=DateTime.UtcNow,PriceId="price_test",MonthlyYen=980,ReturnOrigin="https://localhost"};
    private static string Session(bool live=false,string attempt="attempt-1",string? subscription=null)=>JsonSerializer.Serialize(new{
        id="cs_test_unit",@object="checkout.session",livemode=live,mode="subscription",client_reference_id="attempt-1",
        metadata=new{taskboard_team="1",taskboard_attempt=attempt},status=subscription==null?"open":"complete",subscription,url="https://checkout.stripe.com/c/pay/cs_test_unit"});
    private static StripeTestGateway Gateway(Handler handler)=>new(Options.Create(TeamBillingTests.Config()),new StripeClient("sk_test_unit",httpClient:new SystemNetHttpClient(new HttpClient(handler),maxNetworkRetries:0)));
    [Fact] public async Task Checkout_uses_stored_price_metadata_and_idempotency_key_not_browser_amounts()
    {
        using var handler=new Handler(_=>Session());var gateway=Gateway(handler);var row=Record();
        await gateway.CheckoutAsync(row,default);await gateway.CheckoutAsync(row,default);
        Assert.Equal(handler.Requests[0].Key,handler.Requests[1].Key);
        Assert.Contains("price_test",handler.Requests[0].Body);Assert.Contains("subscription",handler.Requests[0].Body);Assert.DoesNotContain("unit_amount",handler.Requests[0].Body);
        Assert.Contains("taskboard_attempt",handler.Requests[0].Body);Assert.DoesNotContain("sk_test",handler.Requests[0].Body);
    }
    [Fact] public async Task Gateway_rejects_live_mode_and_wrong_team_metadata()
    {
        foreach(var json in new[]{Session(live:true),Session(attempt:"someone-else")})
        {using var handler=new Handler(_=>json);await Assert.ThrowsAsync<TeamOperationException>(()=>Gateway(handler).CheckoutAsync(Record(),default));}
    }
    [Fact] public async Task New_account_metadata_is_bound_to_the_purchaser_and_legacy_metadata_cannot_authorize_it()
    {
        using var handler=new Handler(_=>Session().Replace("taskboard_team","taskboard_account").Replace("\"1\"","\"7\""));
        var row=Record();row.MetadataScope="account";
        await Gateway(handler).CheckoutAsync(row,default);
        Assert.Contains("taskboard_account",handler.Requests[0].Body);
        Assert.DoesNotContain("taskboard_team",handler.Requests[0].Body);
        Assert.Contains("account%3D1",handler.Requests[0].Body);
        using var legacy=new Handler(_=>Session());
        await Assert.ThrowsAsync<TeamOperationException>(()=>Gateway(legacy).CheckoutAsync(row,default));
        row.UserProfileId=8;
        await Assert.ThrowsAsync<TeamOperationException>(()=>Gateway(handler).CheckoutAsync(row,default));
    }
    [Fact] public async Task Ambiguous_checkout_older_than_idempotency_window_cannot_create_again()
    {
        using var handler=new Handler(_=>Session());var row=Record();row.AttemptStartedAt=DateTime.UtcNow.AddDays(-2);
        Assert.Equal("billing_recovery_required",(await Assert.ThrowsAsync<TeamOperationException>(()=>Gateway(handler).CheckoutAsync(row,default))).Code);Assert.Empty(handler.Requests);
    }
    [Fact] public async Task Unpaid_invoice_does_not_grant_access_even_for_active_subscription()
    {
        foreach(var paid in new[]{false,true})
        {
            var end=DateTimeOffset.UtcNow.AddDays(30).ToUnixTimeSeconds();
            using var handler=new Handler(request=>request.RequestUri!.AbsolutePath.Contains("/subscriptions/")?JsonSerializer.Serialize(new{
                id="sub_test",@object="subscription",livemode=false,status="active",metadata=new{taskboard_team="1",taskboard_attempt="attempt-1"},
                items=new{@object="list",data=new[]{new{id="si_test",@object="subscription_item",quantity=1,current_period_end=end,price=new{id="price_test",@object="price",livemode=false,active=true,currency="jpy",unit_amount=980,type="recurring",recurring=new{interval="month",interval_count=1}}}}},
                latest_invoice=new{id="in_test",@object="invoice",livemode=false,status=paid?"paid":"open",amount_paid=paid?980:0,currency="jpy"}
            }):Session(subscription:"sub_test"));
            var row=Record();row.SessionId="cs_test_unit";var result=await Gateway(handler).RefreshAsync(row,default);
            Assert.Equal(paid,result.PaidThrough.HasValue);
        }
    }
}
