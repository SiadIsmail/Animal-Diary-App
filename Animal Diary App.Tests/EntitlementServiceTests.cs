namespace Animal_Diary_App.Tests;

using Animal_Diary_App.Data.Services.Billing;
using Xunit;

/// <summary>
/// The gate: <c>HasFullAccess = store entitlement active OR grant running</c>, and the
/// four states that describe it.
///
/// <para>The most important property in this file is a negative one: <b>the free tier is
/// not a locked state</b>. It has no clock, no expiry and nothing to run out, and it never
/// stops anyone writing anything down. The tests that used to live here: a trial window,
/// its boundary, the read-only state on the far side of it: are gone with the trial.</para>
/// </summary>
public class EntitlementServiceTests
{
    private static readonly DateTime T0 = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>Build an EntitlementService with a fixed clock at T0 and a store state.
    /// No sponsorship: this suite is about your OWN access, which must be decided without
    /// consulting the cloud at all. Sponsorship has its own suite.</summary>
    private static (EntitlementService ent, FakeStore store) Build(
        bool entitled = false, bool known = true, FakeGrants? grants = null)
    {
        var store = new FakeStore { EntitlementKnown = known, HasActiveEntitlement = entitled };
        return (new EntitlementService(
            store, new NullPetAccessSource(), grants ?? new FakeGrants(() => T0), () => T0), store);
    }

    // ── the four states ───────────────────────────────────────────────────────

    [Fact]
    public void NoSubscriptionAndNoGrant_IsFree_NotLocked()
    {
        var (ent, _) = Build();

        Assert.Equal(AccessState.Free, ent.State);
        Assert.False(ent.HasFullAccess);
        // Free is a tier, not an expiry: nothing here says anything ended.
        Assert.False(ent.EverGranted);
        Assert.Null(ent.GrantedUntilUtc);
    }

    [Fact]
    public void Subscribed_GrantsAccess_StateSubscribed()
    {
        var (ent, _) = Build(entitled: true);
        Assert.True(ent.HasFullAccess);
        Assert.Equal(AccessState.Subscribed, ent.State);
    }

    [Fact]
    public void EntitlementUnknown_StaysOptimistic_StateUnknown()
    {
        // During the launch fetch the entitlement is unknown, so a paying subscriber must
        // never be shown a paid surface withheld in that window.
        var (ent, _) = Build(known: false);
        Assert.True(ent.HasFullAccess);
        Assert.Equal(AccessState.Unknown, ent.State);
    }

    [Fact]
    public void TheFreeTier_HasNoClock_SoRepeatedReadsNeverChange()
    {
        // The trial's whole hazard was that the answer changed with the wall clock. This
        // asserts the replacement has no such term: a year later, Free is still Free.
        var store = new FakeStore { EntitlementKnown = true, HasActiveEntitlement = false };
        var now = T0;
        var ent = new EntitlementService(
            store, new NullPetAccessSource(), new FakeGrants(() => now), () => now);

        Assert.Equal(AccessState.Free, ent.State);
        now = T0.AddYears(1);
        Assert.Equal(AccessState.Free, ent.State);
        Assert.False(ent.HasFullAccess);
    }

    // ── access-code grants ────────────────────────────────────────────────────

    [Fact]
    public void Grant_Running_UnlocksWithNoSubscription()
    {
        var grants = new FakeGrants(() => T0).Until(T0.AddDays(200));
        var (ent, _) = Build(grants: grants);

        Assert.True(ent.HasFullAccess);
        Assert.Equal(AccessState.Granted, ent.State);
        Assert.Equal(T0.AddDays(200), ent.GrantedUntilUtc);
    }

    [Fact]
    public void Grant_Expired_FallsBackToFree_ButEverGrantedStaysTrue()
    {
        // EverGranted is what selects the grant copy. Someone whose redeemed year ran out
        // did not cancel anything, and must never be addressed as though they had.
        var grants = new FakeGrants(() => T0).Expired(T0.AddDays(-1));
        var (ent, _) = Build(grants: grants);

        Assert.False(ent.HasFullAccess);
        Assert.Equal(AccessState.Free, ent.State);
        Assert.True(ent.EverGranted);
    }

    [Fact]
    public void GrantUnknown_StaysOptimistic_StateUnknown()
    {
        // Mirrors EntitlementUnknown: a granted user on a second device must not be asked
        // to subscribe in the seconds before the first fetch lands.
        var grants = new FakeGrants(() => T0) { GrantKnown = false };
        var (ent, _) = Build(grants: grants);

        Assert.True(ent.HasFullAccess);
        Assert.Equal(AccessState.Unknown, ent.State);
    }

    [Fact]
    public void SubscriptionBeatsGrant_ForCopySelection()
    {
        // Ordering is the copy contract: a subscriber who also holds a code is a subscriber,
        // because Settings must offer them store management and a grant has none.
        var grants = new FakeGrants(() => T0).Until(T0.AddDays(300));

        var (granted, _) = Build(grants: grants);
        Assert.Equal(AccessState.Granted, granted.State);

        var (subscribed, _) = Build(entitled: true, grants: grants);
        Assert.Equal(AccessState.Subscribed, subscribed.State);
    }

    [Fact]
    public void Grant_ReachesYourOwnPets_UnlikeSponsorship()
    {
        // Sponsorship deliberately never covers a pet you own. A grant is YOUR OWN access,
        // so it must: CanEditPet short-circuits on HasFullAccess before any cloud state.
        var grants = new FakeGrants(() => T0).Until(T0.AddDays(30));
        var (ent, _) = Build(grants: grants);

        Assert.True(ent.CanEditPet("my-own-pet"));
        Assert.True(ent.CanEditPet(null));
    }

    [Fact]
    public async Task Attribution_ReachesTheStore_ButChangesNoAccess()
    {
        // A creator code is a marketing tag routed through this boundary only because the
        // store seam lives behind it. If it ever starts moving the gate, that is a bug: the
        // value is typed into a box by the user and grants nothing.
        var (ent, store) = Build();
        var before = ent.HasFullAccess;
        var stateBefore = ent.State;

        await ent.SetAttributionAsync("THETO");

        Assert.Equal("THETO", store.AttributedTo);
        Assert.Equal(before, ent.HasFullAccess);
        Assert.Equal(stateBefore, ent.State);
    }

    // ── purchase / restore ────────────────────────────────────────────────────

    [Fact]
    public async Task Purchase_Success_UnlocksAndBecomesSubscribed()
    {
        var (ent, store) = Build();
        store.PurchaseResult = PurchaseOutcome.Success;

        var outcome = await ent.PurchaseAsync(SubscriptionPlan.Yearly);

        Assert.Equal(PurchaseOutcome.Success, outcome);
        Assert.True(ent.HasFullAccess);
        Assert.Equal(AccessState.Subscribed, ent.State);
    }

    [Fact]
    public async Task Purchase_Pending_DoesNotUnlock()
    {
        var (ent, store) = Build();
        store.PurchaseResult = PurchaseOutcome.Pending;

        var outcome = await ent.PurchaseAsync(SubscriptionPlan.Monthly);

        Assert.Equal(PurchaseOutcome.Pending, outcome);
        Assert.False(ent.HasFullAccess);
    }

    [Fact]
    public async Task Restore_NothingToRestore_DoesNotUnlock()
    {
        var (ent, store) = Build();
        store.RestoreResult = PurchaseOutcome.NothingToRestore;

        var outcome = await ent.RestoreAsync();

        Assert.Equal(PurchaseOutcome.NothingToRestore, outcome);
        Assert.False(ent.HasFullAccess);
    }

    [Fact]
    public void StateChanged_BubblesFromStore()
    {
        var (ent, store) = Build();
        var fired = 0;
        ent.StateChanged += () => fired++;

        store.RaiseChanged();

        Assert.True(fired >= 1);
    }

    [Fact]
    public void Offers_PassThroughFromStore()
    {
        var (ent, store) = Build();
        store.OfferList.Add(new SubscriptionOffer(SubscriptionPlan.Yearly, "€35.00", "sku"));

        Assert.Single(ent.Offers);
        Assert.Equal("€35.00", ent.Offers[0].PriceLabel);
    }

    [Fact]
    public async Task GetManagementUrl_DelegatesToStore()
    {
        var (ent, store) = Build();
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
