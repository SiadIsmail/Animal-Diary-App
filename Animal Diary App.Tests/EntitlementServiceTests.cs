namespace Animal_Diary_App.Tests;

using Animal_Diary_App.Data.Services.Billing;
using Xunit;

public class EntitlementServiceTests
{
    private static readonly DateTime T0 = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>Build an EntitlementService with a fixed clock at T0 and a trial that is
    /// either active (started now) or expired (started before the window), plus a store
    /// state.</summary>
    private static async Task<(EntitlementService ent, FakeStore store)> BuildAsync(
        bool trialActive, bool entitled = false, bool known = true)
    {
        var store = new FakeStore { EntitlementKnown = known, HasActiveEntitlement = entitled };
        var tstore = new FakeTrialStore
        {
            Start = trialActive ? T0 : T0 - BillingConfig.TrialLength - TimeSpan.FromMinutes(1),
        };
        var trial = new TrialService(tstore, () => T0);
        await trial.InitializeAsync();
        // No sponsorship in these cases: this suite is about your OWN access, which must be
        // decided without consulting the cloud at all. Sponsorship has its own suite.
        return (new EntitlementService(trial, store, new NullPetAccessSource(), () => T0), store);
    }

    [Fact]
    public async Task TrialActive_GrantsAccess_StateTrial()
    {
        var (ent, _) = await BuildAsync(trialActive: true);
        Assert.True(ent.HasFullAccess);
        Assert.Equal(AccessState.Trial, ent.State);
    }

    [Fact]
    public async Task Subscribed_GrantsAccess_StateSubscribed()
    {
        var (ent, _) = await BuildAsync(trialActive: false, entitled: true);
        Assert.True(ent.HasFullAccess);
        Assert.Equal(AccessState.Subscribed, ent.State);
    }

    [Fact]
    public async Task Expired_NoSubscription_Locks_StateTrialExpired()
    {
        var (ent, _) = await BuildAsync(trialActive: false, entitled: false, known: true);
        Assert.False(ent.HasFullAccess);
        Assert.Equal(AccessState.TrialExpired, ent.State);
    }

    [Fact]
    public async Task EntitlementUnknown_StaysOptimistic_StateUnknown()
    {
        // H1: during the launch fetch the entitlement is unknown → a possibly-paying user
        // must NOT be locked, even though the trial has elapsed.
        var (ent, _) = await BuildAsync(trialActive: false, entitled: false, known: false);
        Assert.True(ent.HasFullAccess);
        Assert.Equal(AccessState.Unknown, ent.State);
    }

    [Fact]
    public async Task Purchase_Success_UnlocksAndBecomesSubscribed()
    {
        var (ent, store) = await BuildAsync(trialActive: false);
        store.PurchaseResult = PurchaseOutcome.Success;

        var outcome = await ent.PurchaseAsync(SubscriptionPlan.Yearly);

        Assert.Equal(PurchaseOutcome.Success, outcome);
        Assert.True(ent.HasFullAccess);
        Assert.Equal(AccessState.Subscribed, ent.State);
    }

    [Fact]
    public async Task Purchase_Pending_DoesNotUnlock()
    {
        var (ent, store) = await BuildAsync(trialActive: false);
        store.PurchaseResult = PurchaseOutcome.Pending;

        var outcome = await ent.PurchaseAsync(SubscriptionPlan.Monthly);

        Assert.Equal(PurchaseOutcome.Pending, outcome);
        Assert.False(ent.HasFullAccess);
    }

    [Fact]
    public async Task Restore_NothingToRestore_DoesNotUnlock()
    {
        var (ent, store) = await BuildAsync(trialActive: false);
        store.RestoreResult = PurchaseOutcome.NothingToRestore;

        var outcome = await ent.RestoreAsync();

        Assert.Equal(PurchaseOutcome.NothingToRestore, outcome);
        Assert.False(ent.HasFullAccess);
    }

    [Fact]
    public async Task StateChanged_BubblesFromStore()
    {
        var (ent, store) = await BuildAsync(trialActive: true);
        var fired = 0;
        ent.StateChanged += () => fired++;

        store.RaiseChanged();

        Assert.True(fired >= 1);
    }

    [Fact]
    public async Task Offers_PassThroughFromStore()
    {
        var (ent, store) = await BuildAsync(trialActive: true);
        store.OfferList.Add(new SubscriptionOffer(SubscriptionPlan.Yearly, "€24.99", "sku"));

        Assert.Single(ent.Offers);
        Assert.Equal("€24.99", ent.Offers[0].PriceLabel);
    }

    [Fact]
    public async Task GetManagementUrl_DelegatesToStore()
    {
        var (ent, store) = await BuildAsync(trialActive: true);
        store.ManagementUrl = "https://store/manage/sub";

        Assert.Equal("https://store/manage/sub", await ent.GetManagementUrlAsync());
    }

    [Fact]
    public void NullEntitlementService_AlwaysGrantsAccess()
    {
        var ent = new NullEntitlementService();
        Assert.True(ent.HasFullAccess);
        Assert.Equal(AccessState.Subscribed, ent.State);
    }

    [Fact]
    public async Task NullEntitlementService_PurchaseUnavailable()
    {
        var ent = new NullEntitlementService();
        Assert.Equal(PurchaseOutcome.Unavailable, await ent.PurchaseAsync(SubscriptionPlan.Yearly));
    }
}
