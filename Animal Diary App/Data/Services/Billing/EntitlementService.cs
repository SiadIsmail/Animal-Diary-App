namespace Animal_Diary_App.Data.Services.Billing;

using System.Diagnostics;

/// <summary>
/// The real entitlement boundary on Android/iOS: composes the store's
/// <see cref="IStoreBilling"/> with a redeemed access code and exposes the single gate.
/// The formula lives here and nowhere else:
/// <c>HasFullAccess = store entitlement active OR grant running</c>.
///
/// <para>There is no trial term, and there is no free-tier term either: the free tier is
/// simply the absence of both, and it gates nothing anyone writes down. See
/// <see cref="IEntitlementService"/> for what this boundary does and does not sell.</para>
/// </summary>
public sealed class EntitlementService : IEntitlementService
{
    private readonly IStoreBilling _store;
    private readonly IPetAccessSource _access;
    private readonly IGrantSource _grants;
    private readonly Func<DateTime> _utcNow;

    /// <param name="access">Cloud sponsorship cache. <see cref="NullPetAccessSource"/>
    /// wherever there is no cloud, which makes <see cref="CanEditPet"/> collapse to
    /// <see cref="HasFullAccess"/>.</param>
    /// <param name="grants">Redeemed access codes. <see cref="NullGrantSource"/> wherever
    /// there is no cloud, which removes the term from the formula entirely.</param>
    /// <param name="utcNow">Clock, injectable so the sponsorship grace window is testable.</param>
    public EntitlementService(
        IStoreBilling store,
        IPetAccessSource access,
        IGrantSource grants,
        Func<DateTime>? utcNow = null)
    {
        _store = store;
        _access = access;
        _grants = grants;
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
        // The store can change the entitlement underneath us (restore on another
        // device, a renewal, an expiry push): bubble it up as our own change.
        _store.Changed += () => StateChanged?.Invoke();
    }

    // Optimistic-until-known: while the store hasn't confirmed the entitlement yet (the
    // brief launch fetch), keep access open so a paying subscriber never sees a paid
    // surface offered back to them in that window. The grant fetch gets the same treatment
    // for the same reason: a granted user on a second device must not be asked to
    // subscribe before it lands.
    public bool HasFullAccess =>
        _store.HasActiveEntitlement
        || _grants.IsGranted
        || !_store.EntitlementKnown
        || !_grants.GrantKnown;

    // Ordering is deliberate and is the copy contract, not a preference: a subscriber who
    // also holds a code is a SUBSCRIBER (Settings must offer them store management), and a
    // grant outranks nothing else, so it comes next. Everything else is Free: a tier, not
    // an expiry, and copy must never describe it as one.
    public AccessState State =>
        !_store.EntitlementKnown || !_grants.GrantKnown ? AccessState.Unknown
        : _store.HasActiveEntitlement ? AccessState.Subscribed
        : _grants.IsGranted ? AccessState.Granted
        : AccessState.Free;

    public DateTime? GrantedUntilUtc => _grants.GrantedUntilUtc;

    public bool EverGranted => _grants.EverGranted;

    public bool CanEditPet(string? petSyncId)
    {
        // Your own access first, and without touching cloud state at all: a local-only
        // subscriber has no memberships, no session and no sync, and must never be routed
        // through anything that could throw or block.
        if (HasFullAccess)
            return true;

        // Still waiting on the first access fetch: stay open, exactly as the entitlement
        // does above. Signed-out / backup-off / cloud-disabled report Known=true, so this
        // can never hold a paid surface open for a local-only user.
        if (!_access.AccessKnown)
            return true;

        if (_access.GetPetAccess(petSyncId) is not PetAccessInfo info)
            return false;

        // Sponsorship covers caregivers only. On a pet you OWN, your own (already-failed)
        // access is the whole answer, otherwise a subscription could be laundered into
        // free access for the sponsor's own record.
        if (!info.IsCaregiver || !info.OwnerHasAccess)
            return false;

        return _utcNow() - info.FetchedUtc < BillingConfig.SponsorshipOfflineGrace;
    }

    public IReadOnlyList<SubscriptionOffer> Offers => _store.Offers;

    public event Action? StateChanged;

    public async Task InitializeAsync()
    {
        try { await _store.InitializeAsync(); }
        catch (Exception ex) { Debug.WriteLine($"[Billing] store init failed: {ex.Message}"); }
        // Loads the cached grant before anything can read the gate, then tries the server.
        try { await _grants.RefreshAsync(); }
        catch (Exception ex) { Debug.WriteLine($"[Billing] grant init failed: {ex.Message}"); }
        StateChanged?.Invoke();
    }

    public async Task RefreshAsync()
    {
        try { await _store.RefreshAsync(); }
        catch (Exception ex) { Debug.WriteLine($"[Billing] store refresh failed: {ex.Message}"); }
        // Same call site as the store, so the grant has no trigger of its own to maintain:
        // launch and resume already refresh entitlements. A failure here is silent and keeps
        // the cached grant, which is what lets a granted user work offline.
        try { await _grants.RefreshAsync(); }
        catch (Exception ex) { Debug.WriteLine($"[Billing] grant refresh failed: {ex.Message}"); }
        StateChanged?.Invoke();
    }

    public async Task IdentifyAsync(string? accountId)
    {
        try { await _store.IdentifyAsync(accountId); }
        catch (Exception ex) { Debug.WriteLine($"[Billing] identify failed: {ex.Message}"); }
        StateChanged?.Invoke();
    }

    public async Task SetAttributionAsync(string? creatorCode)
    {
        // No StateChanged: attribution changes nothing about access, and raising it would
        // make every bound surface re-read the gate for a marketing tag.
        try { await _store.SetAttributionAsync(creatorCode); }
        catch (Exception ex) { Debug.WriteLine($"[Billing] attribution failed: {ex.Message}"); }
    }

    public async Task RefreshOffersAsync()
    {
        try { await _store.RefreshOffersAsync(); }
        catch (Exception ex) { Debug.WriteLine($"[Billing] store offers refresh failed: {ex.Message}"); }
        StateChanged?.Invoke();
    }

    public async Task<PurchaseOutcome> PurchaseAsync(SubscriptionPlan plan)
    {
        PurchaseOutcome outcome;
        try { outcome = await _store.PurchaseAsync(plan); }
        catch (Exception ex) { Debug.WriteLine($"[Billing] purchase failed: {ex.Message}"); outcome = PurchaseOutcome.Failed; }
        StateChanged?.Invoke();
        return outcome;
    }

    public async Task<PurchaseOutcome> RestoreAsync()
    {
        PurchaseOutcome outcome;
        try { outcome = await _store.RestoreAsync(); }
        catch (Exception ex) { Debug.WriteLine($"[Billing] restore failed: {ex.Message}"); outcome = PurchaseOutcome.Failed; }
        StateChanged?.Invoke();
        return outcome;
    }

    public async Task<string?> GetManagementUrlAsync()
    {
        try { return await _store.GetManagementUrlAsync(); }
        catch (Exception ex) { Debug.WriteLine($"[Billing] management url failed: {ex.Message}"); return null; }
    }
}
