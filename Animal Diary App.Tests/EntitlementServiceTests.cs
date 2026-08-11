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
        bool trialActive, bool entitled = false, bool known = true, FakeGrants? grants = null)
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
        return (new EntitlementService(
            trial, store, new NullPetAccessSource(), grants ?? new FakeGrants(() => T0), () => T0), store);
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

    // ── access-code grants ────────────────────────────────────────────────────

    [Fact]
    public async Task Grant_Running_UnlocksWithNoTrialAndNoSubscription()
    {
        var grants = new FakeGrants(() => T0).Until(T0.AddDays(200));
        var (ent, _) = await BuildAsync(trialActive: false, grants: grants);

        Assert.True(ent.HasFullAccess);
        Assert.Equal(AccessState.Granted, ent.State);
        Assert.Equal(T0.AddDays(200), ent.GrantedUntilUtc);
    }

    [Fact]
    public async Task Grant_Expired_Locks_ButEverGrantedStaysTrue()
    {
        // EverGranted is what stops the read-only copy telling someone whose YEAR ran out
        // that their 14-day trial has ended.
        var grants = new FakeGrants(() => T0).Expired(T0.AddDays(-1));
        var (ent, _) = await BuildAsync(trialActive: false, grants: grants);

        Assert.False(ent.HasFullAccess);
        Assert.Equal(AccessState.TrialExpired, ent.State);
        Assert.True(ent.EverGranted);
    }

    [Fact]
    public async Task GrantUnknown_StaysOptimistic_StateUnknown()
    {
        // Mirrors EntitlementUnknown: a granted user on a second device must not flash into
        // the read-only state in the seconds before the first fetch lands.
        var grants = new FakeGrants(() => T0) { GrantKnown = false };
        var (ent, _) = await BuildAsync(trialActive: false, grants: grants);

        Assert.True(ent.HasFullAccess);
        Assert.Equal(AccessState.Unknown, ent.State);
    }

    [Fact]
    public async Task Grant_BeatsTrial_ButSubscriptionBeatsGrant()
    {
        // Ordering is the copy contract: a subscriber who also holds a code is a subscriber
        // (Settings must offer them store management), and someone with a redeemed year is
        // not "on a free trial".
        var grants = new FakeGrants(() => T0).Until(T0.AddDays(300));

        var (granted, _) = await BuildAsync(trialActive: true, grants: grants);
        Assert.Equal(AccessState.Granted, granted.State);

        var (subscribed, _) = await BuildAsync(trialActive: true, entitled: true, grants: grants);
        Assert.Equal(AccessState.Subscribed, subscribed.State);
    }

    [Fact]
    public async Task Grant_ReachesYourOwnPets_UnlikeSponsorship()
    {
        // Sponsorship deliberately never covers a pet you own. A grant is YOUR OWN access,
        // so it must — CanEditPet short-circuits on HasFullAccess before any cloud state.
        var grants = new FakeGrants(() => T0).Until(T0.AddDays(30));
        var (ent, _) = await BuildAsync(trialActive: false, grants: grants);

        Assert.True(ent.CanEditPet("my-own-pet"));
        Assert.True(ent.CanEditPet(null));
    }

    [Fact]
    public async Task Attribution_ReachesTheStore_ButChangesNoAccess()
    {
        // A creator code is a marketing tag routed through this boundary only because the
        // store seam lives behind it. If it ever starts moving the gate, that is a bug: the
        // value is typed into a box by the user and grants nothing.
        var (ent, store) = await BuildAsync(trialActive: false);
        var before = ent.HasFullAccess;
        var stateBefore = ent.State;

        await ent.SetAttributionAsync("THETO");

        Assert.Equal("THETO", store.AttributedTo);
        Assert.Equal(before, ent.HasFullAccess);
        Assert.Equal(stateBefore, ent.State);
    }

    [Fact]
    public async Task NoGrant_FallsThroughToTheOtherSources()
    {
        var (trialing, _) = await BuildAsync(trialActive: true, grants: new FakeGrants(() => T0));
        Assert.True(trialing.HasFullAccess);
        Assert.Equal(AccessState.Trial, trialing.State);
        Assert.False(trialing.EverGranted);
        Assert.Null(trialing.GrantedUntilUtc);
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
