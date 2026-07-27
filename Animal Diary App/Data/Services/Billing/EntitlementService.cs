namespace Animal_Diary_App.Data.Services.Billing;

using System.Diagnostics;

/// <summary>
/// The real entitlement boundary on Android/iOS: composes the app-side
/// <see cref="TrialService"/> with the store's <see cref="IStoreBilling"/> and exposes
/// the single gate. This is where the reversible formula lives and nowhere else:
/// <c>HasFullAccess = trial running OR store entitlement active</c>.
/// </summary>
public sealed class EntitlementService : IEntitlementService
{
    private readonly TrialService _trial;
    private readonly IStoreBilling _store;

    public EntitlementService(TrialService trial, IStoreBilling store)
    {
        _trial = trial;
        _store = store;
        // The store can change the entitlement underneath us (restore on another
        // device, a renewal, an expiry push) — bubble it up as our own change.
        _store.Changed += () => StateChanged?.Invoke();
    }

    public bool HasFullAccess => _trial.IsActive || _store.HasActiveEntitlement;

    public AccessState State =>
        _store.HasActiveEntitlement ? AccessState.Subscribed
        : _trial.IsActive ? AccessState.Trial
        : AccessState.TrialExpired;

    public int TrialDaysLeft => _trial.DaysLeft;

    public IReadOnlyList<SubscriptionOffer> Offers => _store.Offers;

    public event Action? StateChanged;

    public async Task InitializeAsync()
    {
        await _trial.InitializeAsync();
        try { await _store.InitializeAsync(); }
        catch (Exception ex) { Debug.WriteLine($"[Billing] store init failed: {ex.Message}"); }
        StateChanged?.Invoke();
    }

    public Task<bool> EnsureTrialStartedAsync() => _trial.EnsureStartedAsync();

    public async Task RefreshAsync()
    {
        try { await _store.RefreshAsync(); }
        catch (Exception ex) { Debug.WriteLine($"[Billing] store refresh failed: {ex.Message}"); }
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
}
