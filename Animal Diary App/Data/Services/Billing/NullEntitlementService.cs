namespace Animal_Diary_App.Data.Services.Billing;

/// <summary>
/// The "no monetization" stand-in — always full access, no trial, no store. Registered
/// when <see cref="BillingConfig.Enabled"/> is false (the default until a RevenueCat key
/// and binding exist) and on platforms without a store (Windows/macOS dev). Mirrors
/// <c>NullCloudSyncService</c> / <c>NullAnalyticsService</c>: with this in place the app
/// carries zero monetization behaviour and can never lock.
/// </summary>
public sealed class NullEntitlementService : IEntitlementService
{
    public bool HasFullAccess => true;
    public bool CanEditPet(string? petSyncId) => true;
    public AccessState State => AccessState.Subscribed;
    public bool TrialEverStarted => false;
    public int TrialDaysLeft => 0;
    public TimeSpan TrialTimeRemaining => TimeSpan.Zero;
    public IReadOnlyList<SubscriptionOffer> Offers => Array.Empty<SubscriptionOffer>();

#pragma warning disable CS0067 // Never raised: access never changes here.
    public event Action? StateChanged;
#pragma warning restore CS0067

    public Task InitializeAsync() => Task.CompletedTask;
    public Task<bool> EnsureTrialStartedAsync() => Task.FromResult(false);
    public Task RefreshAsync() => Task.CompletedTask;
    public Task IdentifyAsync(string? accountId) => Task.CompletedTask;
    public Task RefreshOffersAsync() => Task.CompletedTask;
    public Task<PurchaseOutcome> PurchaseAsync(SubscriptionPlan plan) => Task.FromResult(PurchaseOutcome.Unavailable);
    public Task<PurchaseOutcome> RestoreAsync() => Task.FromResult(PurchaseOutcome.Unavailable);
    public Task<string?> GetManagementUrlAsync() => Task.FromResult<string?>(null);
}
