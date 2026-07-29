namespace Animal_Diary_App.Tests;

using Animal_Diary_App.Data.Services.Billing;
using Xunit;

/// <summary>
/// The sponsorship rule: a caregiver may write to someone else's pet while THAT owner has
/// access, and sponsorship never reaches a pet you own yourself.
///
/// <para>The last property is the one holding the business model up — without it, one
/// subscription plus invite codes becomes unlimited free accounts — so it is tested from
/// several directions rather than once.</para>
/// </summary>
public class SponsoredAccessTests
{
    private static readonly DateTime Now = new(2026, 7, 29, 12, 0, 0, DateTimeKind.Utc);
    private const string TheirPet = "pet-owned-by-someone-else";
    private const string MyPet = "pet-i-own";

    /// <summary>Builds a service whose OWN access has run out — the only interesting
    /// starting point, since anyone with their own access passes everything trivially.</summary>
    private static EntitlementService Locked(FakePetAccess access, out FakeStore store)
    {
        store = new FakeStore { HasActiveEntitlement = false, EntitlementKnown = true };
        var trial = new TrialService(new FakeTrialStore(), () => Now);   // never started
        return new EntitlementService(trial, store, access, () => Now);
    }

    // ── the rule ────────────────────────────────────────────────────────────

    [Fact]
    public void Caregiver_may_edit_a_pet_whose_owner_has_access()
    {
        var gate = Locked(new FakePetAccess().Sponsored(TheirPet, Now), out _);

        Assert.False(gate.HasFullAccess);          // no access of their own
        Assert.True(gate.CanEditPet(TheirPet));    // but covered on this pet
    }

    [Fact]
    public void Caregiver_may_not_edit_once_that_owner_lapses()
    {
        var gate = Locked(new FakePetAccess().Lapsed(TheirPet, Now), out _);
        Assert.False(gate.CanEditPet(TheirPet));
    }

    [Fact]
    public void Sponsorship_never_covers_a_pet_you_own()
    {
        // The load-bearing test. A row for a pet you own carries OwnerHasAccess = your own
        // state; reading it as sponsorship would let anyone unlock themselves.
        var gate = Locked(new FakePetAccess().Owned(MyPet, Now, ownerAccess: true), out _);
        Assert.False(gate.CanEditPet(MyPet));
    }

    [Fact]
    public void Sponsorship_on_one_pet_does_not_leak_to_another()
    {
        var access = new FakePetAccess().Sponsored(TheirPet, Now).Owned(MyPet, Now);
        var gate = Locked(access, out _);

        Assert.True(gate.CanEditPet(TheirPet));
        Assert.False(gate.CanEditPet(MyPet));
        Assert.False(gate.CanEditPet("some-unknown-pet"));
        Assert.False(gate.CanEditPet(null));
    }

    [Fact]
    public void Two_owners_are_judged_independently()
    {
        // A caregiver for two households: one owner pays, the other does not.
        var access = new FakePetAccess().Sponsored("paying-owners-pet", Now).Lapsed("lapsed-owners-pet", Now);
        var gate = Locked(access, out _);

        Assert.True(gate.CanEditPet("paying-owners-pet"));
        Assert.False(gate.CanEditPet("lapsed-owners-pet"));
    }

    // ── offline grace ───────────────────────────────────────────────────────

    [Fact]
    public void Sponsorship_survives_going_offline_inside_the_grace_window()
    {
        var stale = Now - BillingConfig.SponsorshipOfflineGrace + TimeSpan.FromHours(1);
        var gate = Locked(new FakePetAccess().Sponsored(TheirPet, stale), out _);
        Assert.True(gate.CanEditPet(TheirPet));
    }

    [Fact]
    public void Sponsorship_stops_once_the_cache_is_older_than_the_grace_window()
    {
        var tooStale = Now - BillingConfig.SponsorshipOfflineGrace - TimeSpan.FromHours(1);
        var gate = Locked(new FakePetAccess().Sponsored(TheirPet, tooStale), out _);
        Assert.False(gate.CanEditPet(TheirPet));
    }

    // ── the local-first short-circuit ───────────────────────────────────────

    [Fact]
    public void Your_own_access_answers_before_any_cloud_state_is_consulted()
    {
        // A local-only subscriber has no memberships at all. CanEditPet must not depend on
        // the cloud in any way for them.
        var store = new FakeStore { HasActiveEntitlement = true, EntitlementKnown = true };
        var trial = new TrialService(new FakeTrialStore(), () => Now);
        var gate = new EntitlementService(trial, store, new NullPetAccessSource(), () => Now);

        Assert.True(gate.CanEditPet(MyPet));
        Assert.True(gate.CanEditPet(null));
        Assert.True(gate.HasFullAccess);
    }

    [Fact]
    public void A_signed_out_locked_user_is_actually_locked()
    {
        // NullPetAccessSource reports AccessKnown = true precisely so the optimistic window
        // below cannot hold the read-only state open forever for local-only users.
        var gate = Locked(new FakePetAccess(), out _);
        var offline = new EntitlementService(
            new TrialService(new FakeTrialStore(), () => Now),
            new FakeStore { HasActiveEntitlement = false, EntitlementKnown = true },
            new NullPetAccessSource(), () => Now);

        Assert.False(gate.CanEditPet(MyPet));
        Assert.False(offline.CanEditPet(MyPet));
    }

    [Fact]
    public void The_gate_stays_open_until_the_first_access_fetch_lands()
    {
        // A caregiver who just redeemed an invite must not be locked in the seconds before
        // the first sync — mirrors the EntitlementKnown grace on the store side.
        var access = new FakePetAccess { AccessKnown = false };
        var gate = Locked(access, out _);
        Assert.True(gate.CanEditPet(TheirPet));
    }

    // ── trial-sponsored, and the "never had a trial" state ──────────────────

    [Fact]
    public void An_owner_still_in_their_trial_sponsors_their_caregivers()
    {
        // The owner's trial is evaluated server-side, so from the caregiver's side it looks
        // identical to a subscription: OwnerHasAccess = true.
        var gate = Locked(new FakePetAccess().Sponsored(TheirPet, Now), out _);
        Assert.True(gate.CanEditPet(TheirPet));
    }

    [Fact]
    public void A_caregiver_who_never_owned_a_pet_has_no_trial_of_their_own()
    {
        // The trial starts with your first OWN pet, so copy must never tell this person
        // their trial ended — they never had one.
        var gate = Locked(new FakePetAccess().Sponsored(TheirPet, Now), out _);

        Assert.False(gate.TrialEverStarted);
        Assert.Equal(AccessState.TrialExpired, gate.State);
    }

    [Fact]
    public async Task Starting_a_trial_marks_it_as_ever_started()
    {
        var store = new FakeStore { EntitlementKnown = true };
        var trial = new TrialService(new FakeTrialStore(), () => Now);
        var gate = new EntitlementService(trial, store, new NullPetAccessSource(), () => Now);

        Assert.False(gate.TrialEverStarted);
        Assert.True(await gate.EnsureTrialStartedAsync());
        Assert.True(gate.TrialEverStarted);
        Assert.True(gate.HasFullAccess);
    }

    // ── identity linking ────────────────────────────────────────────────────

    [Fact]
    public async Task Identify_passes_the_account_through_and_clears_it_on_sign_out()
    {
        var store = new FakeStore();
        var gate = new EntitlementService(
            new TrialService(new FakeTrialStore(), () => Now), store, new NullPetAccessSource(), () => Now);

        await gate.IdentifyAsync("user-123");
        Assert.Equal("user-123", store.IdentifiedAs);

        await gate.IdentifyAsync(null);
        Assert.Null(store.IdentifiedAs);
        Assert.Equal(2, store.IdentifyCount);
    }

    [Fact]
    public void The_null_service_never_locks_any_pet()
    {
        var gate = new NullEntitlementService();
        Assert.True(gate.CanEditPet(MyPet));
        Assert.True(gate.CanEditPet(null));
        Assert.False(gate.TrialEverStarted);
    }
}

/// <summary>
/// The trial anchor is reconciled with the account so the SERVER can tell a caregiver
/// whether their pet's owner is still in trial. Reconciliation must be monotone: signing in
/// can shorten a trial but never extend one, or a fresh email address would mint a fresh
/// sponsorship window.
/// </summary>
public class TrialAnchorTests
{
    private static readonly DateTime Now = new(2026, 7, 29, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Adopts_the_accounts_anchor_when_this_device_has_none()
    {
        var store = new FakeTrialStore();
        var trial = new TrialService(store, () => Now);
        await trial.InitializeAsync();

        var accountStart = Now - TimeSpan.FromDays(3);
        await trial.AdoptAsync(accountStart);

        Assert.Equal(accountStart, await trial.GetStartUtcAsync());
        Assert.Equal(accountStart, store.Start);
    }

    [Fact]
    public async Task Adopts_an_earlier_anchor_shortening_the_trial()
    {
        var store = new FakeTrialStore { Start = Now };            // started "today" locally
        var trial = new TrialService(store, () => Now);
        var earlier = Now - TimeSpan.FromDays(30);                  // the account knows better

        await trial.AdoptAsync(earlier);

        Assert.Equal(earlier, await trial.GetStartUtcAsync());
        Assert.False(trial.IsActive);   // a 30-day-old anchor is long expired
    }

    [Fact]
    public async Task Never_adopts_a_later_anchor()
    {
        // The abuse path this closes: sign in with a new email, get a fresh trial.
        var original = Now - TimeSpan.FromDays(30);
        var store = new FakeTrialStore { Start = original };
        var trial = new TrialService(store, () => Now);

        await trial.AdoptAsync(Now);   // a "brand new" account anchor

        Assert.Equal(original, await trial.GetStartUtcAsync());
        Assert.False(trial.IsActive);
    }

    [Fact]
    public async Task A_null_account_anchor_changes_nothing()
    {
        var store = new FakeTrialStore { Start = Now };
        var trial = new TrialService(store, () => Now);

        await trial.AdoptAsync(null);

        Assert.Equal(Now, await trial.GetStartUtcAsync());
    }

    [Fact]
    public async Task GetStartUtcAsync_loads_on_demand()
    {
        // The sync engine can reach the anchor before billing has initialized; a
        // synchronous read would report "no trial" and then never re-claim.
        var trial = new TrialService(new FakeTrialStore { Start = Now }, () => Now);
        Assert.Equal(Now, await trial.GetStartUtcAsync());   // no InitializeAsync first
    }
}
