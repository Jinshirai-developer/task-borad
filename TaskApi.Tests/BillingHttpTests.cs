using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Stripe;

namespace TaskApi.Tests;

public sealed class BillingHttpTests
{
    private static Dictionary<string,string?> Config() => new() { ["Billing:Enabled"]="true",["Billing:SecretKey"]="sk_test_http_fake",["Billing:WebhookSecret"]="whsec_unit_test",["Billing:PriceId"]="price_test" };
    private static HttpRequestMessage Event(string id,string sessionId,string signatureSecret="whsec_unit_test",bool live=false,long? timestamp=null)
    {
        var now=timestamp??DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var json=JsonSerializer.Serialize(new{id,@object="event",api_version=StripeConfiguration.ApiVersion,created=now,livemode=live,type="checkout.session.completed",data=new{@object=new{id=sessionId,@object="checkout.session",livemode=live}}});
        var hash=Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(signatureSecret),Encoding.UTF8.GetBytes($"{now}.{json}"))).ToLowerInvariant();
        var message=new HttpRequestMessage(HttpMethod.Post,"/api/billing/stripe-webhook"){Content=new StringContent(json,Encoding.UTF8,"application/json")};
        message.Headers.Add("Stripe-Signature",$"t={now},v1={hash}");return message;
    }
    [Fact] public async Task Billing_routes_require_authentication_CSRF_and_owner_without_network()
    {
        var gateway=new TeamBillingTests.Gateway();using var factory=new TaskApiFactory(Config(),billingGateway:gateway);
        using var owner=factory.CreateSecureClient();using var member=factory.CreateSecureClient();
        Assert.Equal(HttpStatusCode.Unauthorized,(await owner.GetAsync("/api/teams/1/billing")).StatusCode);
        await factory.RegisterConfirmedAsync(owner);await factory.RegisterConfirmedAsync(member);
        var created=await owner.SendWithCsrfAsync(HttpMethod.Post,"/api/teams",new{name="Billing"});var team=await created.Content.ReadFromJsonAsync<JsonElement>();var id=team.GetProperty("team").GetProperty("id").GetInt32();
        Assert.Equal(HttpStatusCode.NotFound,(await member.GetAsync($"/api/teams/{id}/billing")).StatusCode);
        await member.SendWithCsrfAsync(HttpMethod.Post,"/api/teams/join",new{inviteCode=team.GetProperty("inviteCode").GetString()});
        var path=$"/api/teams/{id}/billing";
        Assert.Equal(HttpStatusCode.BadRequest,(await owner.PostAsJsonAsync(path+"/checkout",new{})).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,(await member.SendWithCsrfAsync(HttpMethod.Post,path+"/checkout",new{})).StatusCode);
        Assert.Equal(HttpStatusCode.OK,(await owner.SendWithCsrfAsync(HttpMethod.Post,path+"/checkout",new{})).StatusCode);
        var status=await owner.GetAsync(path);Assert.Contains("no-store",status.Headers.CacheControl!.ToString());
        Assert.DoesNotContain("sk_test",await status.Content.ReadAsStringAsync());
        Assert.Equal("free",(await status.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("plan").GetString());
        Assert.Single(gateway.Attempts);
    }
    [Fact] public async Task Signed_webhook_bypasses_only_cookie_CSRF_rejects_live_stale_and_forged_then_reconciles_once()
    {
        var gateway=new TeamBillingTests.Gateway();using var factory=new TaskApiFactory(Config(),billingGateway:gateway);
        using var owner=factory.CreateSecureClient();using var anonymous=factory.CreateSecureClient();await factory.RegisterConfirmedAsync(owner);
        var created=await owner.SendWithCsrfAsync(HttpMethod.Post,"/api/teams",new{name="Webhook"});var team=await created.Content.ReadFromJsonAsync<JsonElement>();var id=team.GetProperty("team").GetProperty("id").GetInt32();
        await owner.SendWithCsrfAsync(HttpMethod.Post,$"/api/teams/{id}/billing/checkout",new{});
        Assert.Equal(HttpStatusCode.BadRequest,(await anonymous.SendAsync(Event("evt_fake","cs_test_unit","wrong"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await anonymous.SendAsync(Event("evt_live","cs_test_unit",live:true))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await anonymous.SendAsync(Event("evt_old","cs_test_unit",timestamp:DateTimeOffset.UtcNow.AddHours(-1).ToUnixTimeSeconds()))).StatusCode);
        Assert.Equal(HttpStatusCode.OK,(await anonymous.SendAsync(Event("evt_other_app","cs_test_unknown"))).StatusCode);
        Assert.Equal(0,gateway.Refreshes);
        gateway.Snapshot=new("cs_test_unit","active","sub_test",DateTime.UtcNow.AddDays(30),false);
        Assert.Equal(HttpStatusCode.OK,(await anonymous.SendAsync(Event("evt_paid","cs_test_unit"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK,(await anonymous.SendAsync(Event("evt_paid","cs_test_unit"))).StatusCode);
        Assert.Equal(1,gateway.Refreshes);
        Assert.Equal("pro",(await owner.GetFromJsonAsync<JsonElement>($"/api/teams/{id}/billing")).GetProperty("plan").GetString());
        Assert.Equal(HttpStatusCode.Unauthorized,(await anonymous.PostAsJsonAsync($"/api/teams/{id}/billing/end_now",new{})).StatusCode);
    }
    [Fact] public async Task Disabled_Stripe_is_explicitly_unavailable_not_fake_success()
    {
        using var factory=new TaskApiFactory();using var owner=factory.CreateSecureClient();await factory.RegisterConfirmedAsync(owner);
        var created=await owner.SendWithCsrfAsync(HttpMethod.Post,"/api/teams",new{name="Disabled"});var team=await created.Content.ReadFromJsonAsync<JsonElement>();var id=team.GetProperty("team").GetProperty("id").GetInt32();
        Assert.False((await owner.GetFromJsonAsync<JsonElement>($"/api/teams/{id}/billing")).GetProperty("checkoutAvailable").GetBoolean());
        Assert.Equal(HttpStatusCode.ServiceUnavailable,(await owner.SendWithCsrfAsync(HttpMethod.Post,$"/api/teams/{id}/billing/checkout",new{})).StatusCode);
    }
    [Fact] public async Task Account_route_requires_auth_and_csrf_and_never_reads_another_users_plan()
    {
        var gateway=new TeamBillingTests.Gateway();using var factory=new TaskApiFactory(Config(),billingGateway:gateway);
        using var owner=factory.CreateSecureClient();using var other=factory.CreateSecureClient();
        Assert.Equal(HttpStatusCode.Unauthorized,(await owner.GetAsync("/api/user/billing")).StatusCode);
        await factory.RegisterConfirmedAsync(owner);await factory.RegisterConfirmedAsync(other);
        Assert.Equal(HttpStatusCode.BadRequest,(await owner.PostAsJsonAsync("/api/user/billing/checkout",new{})).StatusCode);
        gateway.Snapshot=new("cs_test_unit","active","sub_test",DateTime.UtcNow.AddDays(30),false);
        Assert.Equal(HttpStatusCode.OK,(await owner.SendWithCsrfAsync(HttpMethod.Post,"/api/user/billing/checkout",new{userProfileId=999})).StatusCode);
        var account=await owner.GetFromJsonAsync<JsonElement>("/api/user/billing");
        Assert.Equal("pro",account.GetProperty("plan").GetString());
        Assert.Equal("account",account.GetProperty("scope").GetString());
        Assert.Equal(0,account.GetProperty("ownedTeamCount").GetInt32());
        Assert.Equal("free",(await other.GetFromJsonAsync<JsonElement>("/api/user/billing")).GetProperty("plan").GetString());
        var created=await owner.SendWithCsrfAsync(HttpMethod.Post,"/api/teams",new{name="After payment"});
        var id=(await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("team").GetProperty("id").GetInt32();
        Assert.Equal("pro",(await owner.GetFromJsonAsync<JsonElement>($"/api/teams/{id}/billing")).GetProperty("plan").GetString());
        Assert.Single(gateway.Attempts);
    }
}
