namespace Animal_Diary_App.Tests;

using Animal_Diary_App.Data.Services.Billing;
using Xunit;

/// <summary>
/// The sponsorship rule: a caregiver reaches someone else's pet's PAID surfaces while THAT
/// owner has access, and sponsorship never reaches a pet you own yourself.
///
/// <para>The last property is the one holding the business model up: without it, one
/// subscription plus invite codes becomes unlimited free accounts, so it is tested from
/// several directions rather than once.</para>
///
/// <para><b>This is no longer a write gate.</b> Every write is free on every tier, so
/// "may edit" here means the paid, pet-scoped surfaces (the assembled summary, the designed
/// report), never logging.</para>
/// </summary>
public class SponsoredAccessTests
{
    private static readonly DateTime Now = new(2026, 7, 29, 12, 0, 0, DateTimeKind.Utc);
    private const string TheirPet = "pet-owned-by-someone-else";
    private const string MyPet = "pet-i-own";

    /// <summary>Builds a service whose OWN access has run out: the only interesting
    /// starting point, since anyone with their own access passes everything trivially.</summary>
    private static EntitlementService Locked(FakePetAccess access, out FakeStore store)
    {
        store = new FakeStore { HasActiveEntitlement = false, EntitlementKnown = true };
        return new EntitlementService(store, access, new NullGrantSource(), () => Now);
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
        var gate = new EntitlementService(store, new NullPetAccessSource(), new NullGrantSource(), () => Now);

        Assert.True(gate.CanEditPet(MyPet));
        Assert.True(gate.CanEditPet(null));
        Assert.True(gate.HasFullAccess);
    }

    [Fact]
    public void A_signed_out_free_user_really_is_on_the_free_tier()
    {
        // NullPetAccessSource reports AccessKnown = true precisely so the optimistic window
        // cannot hold the paid surfaces open forever for local-only users.
        var gate = Locked(new FakePetAccess(), out _);
        var offline = new EntitlementService(
            new FakeStore { HasActiveEntitlement = false, EntitlementKnown = true },
            new NullPetAccessSource(), new NullGrantSource(), () => Now);

        Assert.False(gate.CanEditPet(MyPet));
        Assert.False(offline.CanEditPet(MyPet));
    }

    [Fact]
    public void The_gate_stays_open_until_the_first_access_fetch_lands()
    {
        // A caregiver who just redeemed an invite must not be refused in the seconds before
        // the first sync: mirrors the EntitlementKnown grace on the store side.
        var access = new FakePetAccess { AccessKnown = false };
        var gate = Locked(access, out _);
        Assert.True(gate.CanEditPet(TheirPet));
    }

    [Fact]
    public void An_owner_who_pays_still_sponsors_their_caregivers()
    {
        var gate = Locked(new FakePetAccess().Sponsored(TheirPet, Now), out _);
        Assert.True(gate.CanEditPet(TheirPet));
    }

    // ── identity linking ────────────────────────────────────────────────────

    [Fact]
    public async Task Identify_passes_the_account_through_and_clears_it_on_sign_out()
    {
        var store = new FakeStore();
        var gate = new EntitlementService(store, new NullPetAccessSource(), new NullGrantSource(), () => Now);

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
    }
}
