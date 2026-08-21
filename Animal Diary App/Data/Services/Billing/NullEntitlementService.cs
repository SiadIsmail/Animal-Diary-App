namespace Animal_Diary_App.Data.Services.Billing;

/// <summary>
/// The "no monetization" stand-in: always full access, no store. Registered when
/// <see cref="BillingConfig.Enabled"/> is false and on platforms without a store
/// (Windows/macOS dev). Mirrors <c>NullCloudSyncService</c> / <c>NullAnalyticsService</c>:
/// with this in place the app carries zero monetization behaviour and no paid surface is
/// ever withheld.
/// </summary>
public sealed class NullEntitlementService : IEntitlementService
{
    public bool HasFullAccess => true;
    public bool CanEditPet(string? petSyncId) => true;
    public AccessState State => AccessState.Subscribed;
    public DateTime? GrantedUntilUtc => null;
    public bool EverGranted => false;
    public IReadOnlyList<SubscriptionOffer> Offers => Array.Empty<SubscriptionOffer>();

#pragma warning disable CS0067 // Never raised: access never changes here.
    public event Action? StateChanged;
#pragma warning restore CS0067

    public Task InitializeAsync() => Task.CompletedTask;
    public Task RefreshAsync() => Task.CompletedTask;
    public Task IdentifyAsync(string? accountId) => Task.CompletedTask;
    public Task RefreshOffersAsync() => Task.CompletedTask;
    public Task SetAttributionAsync(string? creatorCode) => Task.CompletedTask;
    public Task<PurchaseOutcome> PurchaseAsync(SubscriptionPlan plan) => Task.FromResult(PurchaseOutcome.Unavailable);
    public Task<PurchaseOutcome> RestoreAsync() => Task.FromResult(PurchaseOutcome.Unavailable);
    public Task<string?> GetManagementUrlAsync() => Task.FromResult<string?>(null);
}
