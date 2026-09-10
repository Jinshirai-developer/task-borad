using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TaskApi.Configuration;
using TaskApi.Data;
using TaskApi.DTOs;
using TaskApi.Models;
using TaskApi.Services;

namespace TaskApi.Tests;

public sealed class TeamBillingTests
{
    [Fact] public void Default_plan_uses_approved_500_yen_test_price_and_stays_disabled()
    {
        var options = new BillingOptions();
        Assert.Equal(500, options.MonthlyYen);
        Assert.False(options.Enabled);
        Assert.False(options.IsConfigured);
    }

    internal static BillingOptions Config(bool enabled = true) => new() { Enabled=enabled,SecretKey="sk_test_not_a_real_key",WebhookSecret="whsec_unit_test",PriceId="price_test",MonthlyYen=980 };
    internal sealed class Gateway : IStripeTestGateway
    {
        public BillingSnapshot Snapshot = new("cs_test_unit", "checkout", null, null, false, "https://checkout.stripe.com/c/pay/cs_test_unit");
        public readonly List<string?> Attempts = [];
        public int Refreshes;
        public bool Fail;
        public bool InvalidPrice;
        public TaskCompletionSource? Wait;
        public Task ValidatePlanAsync(string price, int amount, CancellationToken token) => InvalidPrice ? Task.FromException(new TeamOperationException("billing_mismatch","invalid",409)) : Task.CompletedTask;
        public async Task<BillingSnapshot> CheckoutAsync(TeamBilling record, CancellationToken token) { Attempts.Add(record.AttemptId); if(Wait != null) await Wait.Task; if(Fail) throw new HttpRequestException(); return Snapshot; }
        public Task<BillingSnapshot> RefreshAsync(TeamBilling record,CancellationToken token) { Refreshes++; if(Fail)throw new HttpRequestException();return Task.FromResult(Snapshot); }
        public Task<BillingSnapshot> ChangeAsync(TeamBilling record,string action,CancellationToken token)
        {
            Snapshot = action == "end_now" || action == "abandon" ? Snapshot with { Status="canceled",PaidThrough=null,CancelAtPeriodEnd=false }
                : Snapshot with { CancelAtPeriodEnd=action == "cancel" };
            return Task.FromResult(Snapshot);
        }
    }
    private sealed class Fixture : IDisposable
    {
        public readonly AppDbContext Db = new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        public readonly Gateway Stripe = new();
        public readonly TeamService Teams;
        public readonly TeamBillingService Billing;
        public readonly int Owner;
        public readonly TeamCreatedResponse Team;
        public Fixture(bool enabled = true)
        {
            Teams=new(Db);Owner=User("owner");Team=Teams.Create(Owner,new(){Name="First"});
            Billing=new(Db,Teams,Stripe,Options.Create(Config(enabled)),Options.Create(new Configuration.AuthenticationOptions { PublicBaseUrl="https://localhost" }));
        }
        public int User(string name) => new UserProfileService(Db,NullLogger<UserProfileService>.Instance).GetOrCreateByKey(name).Id;
        public TeamDetailResponse Join(int id) => Teams.Join(id,new(){InviteCode=Team.InviteCode});
        public void Dispose()=>Db.Dispose();
    }
    [Fact] public void Free_cap_counts_owner_and_repeat_join_does_not_consume_another_seat()
    {
        using var f=new Fixture();var a=f.User("a");f.Join(a);f.Join(f.User("b"));
        Assert.Equal(3,f.Join(a).MemberCount);
        Assert.Equal("team_full",Assert.Throws<TeamOperationException>(()=>f.Join(f.User("c"))).Code);
        var state=f.Billing.Get(f.Owner,f.Team.Team.Id);Assert.Equal(3,state.MemberLimit);Assert.False(state.CanJoin);
    }
    [Fact] public void Existing_oversize_free_team_keeps_members_and_tasks()
    {
        using var f=new Fixture();for(var i=0;i<5;i++)f.Db.TeamMembers.Add(new(){TeamId=f.Team.Team.Id,UserProfileId=f.User("legacy"+i)});
        f.Db.Tasks.Add(new(){Title="Keep",TeamId=f.Team.Team.Id,UserProfileId=f.Owner});f.Db.SaveChanges();
        Assert.False(f.Billing.Get(f.Owner,f.Team.Team.Id).CanJoin);Assert.Equal(6,f.Teams.Get(f.Owner,f.Team.Team.Id).MemberCount);Assert.Single(f.Db.Tasks);
    }
    [Fact] public async Task Verified_account_payment_applies_to_existing_and_future_owned_teams()
    {
        using var f=new Fixture();var other=f.Teams.Create(f.Owner,new(){Name="Other"});
        var started=await f.Billing.ExecuteAsync(f.Owner,f.Team.Team.Id,"checkout");Assert.Equal("free",started.Billing.Plan);
        f.Stripe.Snapshot=new("cs_test_unit","active","sub_test",DateTime.UtcNow.AddDays(30),false);
        var synced=await f.Billing.ExecuteAsync(f.Owner,f.Team.Team.Id,"sync");Assert.Equal("pro",synced.Billing.Plan);Assert.Null(synced.Billing.MemberLimit);
        for(var i=0;i<21;i++)f.Join(f.User("new"+i));
        Assert.Equal(22,f.Teams.Get(f.Owner,f.Team.Team.Id).MemberCount);Assert.Equal("pro",f.Billing.Get(f.Owner,other.Team.Id).Plan);
        var future=f.Teams.Create(f.Owner,new(){Name="Created after purchase"});
        Assert.Equal("pro",f.Billing.Get(f.Owner,future.Team.Id).Plan);
        var foreignOwner=f.User("foreign");var foreign=f.Teams.Create(foreignOwner,new(){Name="Other owner"});
        f.Teams.Join(f.Owner,new(){InviteCode=foreign.InviteCode});
        Assert.Equal("free",f.Billing.Get(f.Owner,foreign.Team.Id).Plan);
        Assert.False(f.Billing.Get(f.Owner,foreign.Team.Id).IsOwner);
        Assert.Empty(f.Db.PetProfiles);Assert.Empty(f.Db.CompletionRewards);
    }
    [Fact] public async Task Cancel_at_end_retains_access_then_end_now_preserves_members_and_stops_new_join()
    {
        using var f=new Fixture();f.Stripe.Snapshot=new("cs_test_unit","active","sub_test",DateTime.UtcNow.AddDays(1),false);
        await f.Billing.ExecuteAsync(f.Owner,f.Team.Team.Id,"checkout");for(var i=0;i<3;i++)f.Join(f.User("m"+i));
        var cancelled=await f.Billing.ExecuteAsync(f.Owner,f.Team.Team.Id,"cancel");Assert.True(cancelled.Billing.CancelAtPeriodEnd);Assert.Equal("pro",cancelled.Billing.Plan);
        Assert.False((await f.Billing.ExecuteAsync(f.Owner,f.Team.Team.Id,"resume")).Billing.CancelAtPeriodEnd);
        var ended=await f.Billing.ExecuteAsync(f.Owner,f.Team.Team.Id,"end_now");Assert.Equal("free",ended.Billing.Plan);Assert.Equal(4,ended.Billing.MemberCount);
        Assert.Throws<TeamOperationException>(()=>f.Join(f.User("next")));
    }
    [Fact] public async Task Failed_renewal_preserves_paid_interval_but_never_grants_access_beyond_expiry()
    {
        using var f=new Fixture();f.Stripe.Snapshot=new("cs_test_unit","active","sub_test",DateTime.UtcNow.AddDays(1),false);
        await f.Billing.ExecuteAsync(f.Owner,f.Team.Team.Id,"checkout");
        f.Stripe.Snapshot=f.Stripe.Snapshot with {Status="past_due",PaidThrough=null};
        Assert.Equal("pro",(await f.Billing.ExecuteAsync(f.Owner,f.Team.Team.Id,"sync")).Billing.Plan);
        f.Db.TeamBillings.Single().PaidThrough=DateTime.UtcNow.AddSeconds(-1);f.Db.SaveChanges();
        Assert.Equal("free",f.Billing.Get(f.Owner,f.Team.Team.Id).Plan);
    }
    [Fact] public async Task Lost_response_retries_same_attempt_and_never_unlocks_locally()
    {
        using var f=new Fixture();f.Stripe.Fail=true;
        await Assert.ThrowsAsync<HttpRequestException>(()=>f.Billing.ExecuteAsync(f.Owner,f.Team.Team.Id,"checkout"));
        Assert.Equal("free",f.Billing.Get(f.Owner,f.Team.Team.Id).Plan);Assert.Null(f.Db.TeamBillings.Single().OperationToken);
        f.Stripe.Fail=false;await f.Billing.ExecuteAsync(f.Owner,f.Team.Team.Id,"checkout");Assert.Equal(f.Stripe.Attempts[0],f.Stripe.Attempts[1]);
    }
    [Fact] public async Task Concurrent_checkout_cannot_create_a_second_contract()
    {
        using var f=new Fixture();f.Stripe.Wait=new(TaskCreationOptions.RunContinuationsAsynchronously);
        var other=f.Teams.Create(f.Owner,new(){Name="Other"});
        var first=f.Billing.ExecuteAsync(f.Owner,f.Team.Team.Id,"checkout");
        Assert.Equal("billing_busy",(await Assert.ThrowsAsync<TeamOperationException>(()=>f.Billing.ExecuteAsync(f.Owner,other.Team.Id,"checkout"))).Code);
        Assert.Equal("billing_busy",(await Assert.ThrowsAsync<TeamOperationException>(()=>f.Billing.ExecuteAccountAsync(f.Owner,"checkout"))).Code);
        f.Stripe.Wait.SetResult();await first;Assert.Single(f.Stripe.Attempts);
    }
    [Fact] public async Task Invalid_price_does_not_leave_a_pending_contract()
    {
        using var f=new Fixture();f.Stripe.InvalidPrice=true;
        await Assert.ThrowsAsync<TeamOperationException>(()=>f.Billing.ExecuteAsync(f.Owner,f.Team.Team.Id,"checkout"));Assert.Empty(f.Db.TeamBillings);
    }
    [Fact] public async Task Notifications_reconcile_latest_state_and_duplicate_receipt_has_no_extra_effect()
    {
        using var f=new Fixture();await f.Billing.ExecuteAsync(f.Owner,f.Team.Team.Id,"checkout");
        f.Stripe.Snapshot=new("cs_test_unit","active","sub_test",DateTime.UtcNow.AddDays(1),false);
        await f.Billing.ReconcileAsync(f.Owner,"evt_first");await f.Billing.ReconcileAsync(f.Owner,"evt_first");
        Assert.Equal(1,f.Stripe.Refreshes);Assert.Single(f.Db.BillingEventReceipts);
        f.Stripe.Snapshot=f.Stripe.Snapshot with{Status="canceled",PaidThrough=null};await f.Billing.ReconcileAsync(f.Owner,"evt_new");
        await f.Billing.ReconcileAsync(f.Owner,"evt_old_arrived_late");Assert.Equal("free",f.Billing.Get(f.Owner,f.Team.Team.Id).Plan);
    }
    [Fact] public async Task Failed_notification_is_retryable_and_not_marked_processed()
    {
        using var f=new Fixture();await f.Billing.ExecuteAsync(f.Owner,f.Team.Team.Id,"checkout");f.Stripe.Fail=true;
        await Assert.ThrowsAsync<HttpRequestException>(()=>f.Billing.ReconcileAsync(f.Owner,"evt_retry"));Assert.Empty(f.Db.BillingEventReceipts);
        f.Stripe.Fail=false;await f.Billing.ReconcileAsync(f.Owner,"evt_retry");Assert.Single(f.Db.BillingEventReceipts);
    }
    [Fact] public async Task Members_can_read_but_only_owner_can_manage_and_outsiders_cannot_read()
    {
        using var f=new Fixture();var member=f.User("member");f.Join(member);
        Assert.False(f.Billing.Get(member,f.Team.Team.Id).IsOwner);
        foreach(var action in new[]{"checkout","sync","cancel","resume","end_now","abandon"})
            Assert.Equal(403,(await Assert.ThrowsAsync<TeamOperationException>(()=>f.Billing.ExecuteAsync(member,f.Team.Team.Id,action))).StatusCode);
        Assert.Equal(404,Assert.Throws<TeamOperationException>(()=>f.Billing.Get(f.User("outside"),f.Team.Team.Id)).StatusCode);
    }
    [Fact] public async Task Account_contract_survives_team_deletion_and_never_transfers_to_next_owner()
    {
        using var f=new Fixture();var member=f.User("member");f.Join(member);await f.Billing.ExecuteAsync(f.Owner,f.Team.Team.Id,"checkout");
        f.Stripe.Snapshot=new("cs_test_unit","active","sub_test",DateTime.UtcNow.AddDays(1),false);
        await f.Billing.ExecuteAccountAsync(f.Owner,"sync");
        f.Teams.TransferOwner(f.Owner,f.Team.Team.Id,member);
        Assert.Equal("free",f.Billing.Get(member,f.Team.Team.Id).Plan);
        Assert.Equal("pro",f.Billing.GetAccount(f.Owner).Plan);
        f.Teams.Delete(member,f.Team.Team.Id);
        await f.Billing.ReconcileAsync(f.Owner,"evt_after_team_deleted");
        Assert.Single(f.Db.TeamBillings);Assert.Single(f.Db.BillingEventReceipts);
        Assert.Equal("pro",f.Billing.GetAccount(f.Owner).Plan);
        var users=new UserProfileService(f.Db,NullLogger<UserProfileService>.Instance);
        Assert.Equal("billing_contract_active",Assert.Throws<TeamOperationException>(()=>users.DeleteProfile(f.Owner)).Code);
        await f.Billing.ExecuteAccountAsync(f.Owner,"end_now");
        Assert.True(users.DeleteProfile(f.Owner));
        Assert.Empty(f.Db.TeamBillings);Assert.Empty(f.Db.BillingEventReceipts);
    }
    [Fact] public async Task Account_checkout_works_without_teams_and_reuses_one_attempt_from_any_owned_team()
    {
        using var f=new Fixture();f.Teams.Delete(f.Owner,f.Team.Team.Id);
        await f.Billing.ExecuteAccountAsync(f.Owner,"checkout");
        var first=f.Db.TeamBillings.Single().AttemptId;
        var team=f.Teams.Create(f.Owner,new(){Name="New"});
        await f.Billing.ExecuteAsync(f.Owner,team.Team.Id,"checkout");
        Assert.Single(f.Db.TeamBillings);Assert.Equal(first,f.Db.TeamBillings.Single().AttemptId);
        Assert.Equal(0,f.Db.TeamBillings.Single().TeamId);
        Assert.Equal("account",f.Db.TeamBillings.Single().MetadataScope);
    }
    [Fact] public async Task Missing_connection_disables_checkout_and_does_not_create_records()
    {
        using var f=new Fixture(false);Assert.False(f.Billing.Get(f.Owner,f.Team.Team.Id).CheckoutAvailable);
        Assert.Equal(503,(await Assert.ThrowsAsync<TeamOperationException>(()=>f.Billing.ExecuteAsync(f.Owner,f.Team.Team.Id,"checkout"))).StatusCode);Assert.Empty(f.Db.TeamBillings);
    }
    [Theory]
    [InlineData("sk_live_forbidden")][InlineData("rk_live_forbidden")][InlineData("not-a-key")]
    public void Live_or_unknown_keys_are_rejected_even_when_disabled(string key)
    {var config=Config(false);config.SecretKey=key;Assert.False(config.IsValid());}
}
