namespace Animal_Diary_App.Tests;

using Animal_Diary_App.Data.Services.Billing;

/// <summary>In-memory trial store — no SQLite.</summary>
internal sealed class FakeTrialStore : ITrialStore
{
    public DateTime? Start { get; set; }
    public Task<DateTime?> GetTrialStartUtcAsync() => Task.FromResult(Start);
    public Task SetTrialStartUtcAsync(DateTime startUtc) { Start = startUtc; return Task.CompletedTask; }
}

/// <summary>Scriptable store double — set entitlement/known/offers and the outcomes each
/// call should return.</summary>
internal sealed class FakeStore : IStoreBilling
{
    public bool HasActiveEntitlement { get; set; }
    public bool EntitlementKnown { get; set; } = true;
    public List<SubscriptionOffer> OfferList { get; } = new();
    public IReadOnlyList<SubscriptionOffer> Offers => OfferList;

    public PurchaseOutcome PurchaseResult { get; set; } = PurchaseOutcome.Success;
    public PurchaseOutcome RestoreResult { get; set; } = PurchaseOutcome.Success;
    public string? ManagementUrl { get; set; } = "https://store/manage";

    public int InitCount { get; private set; }
    public int RefreshCount { get; private set; }
    public int RefreshOffersCount { get; private set; }

    public event Action? Changed;
    public void RaiseChanged() => Changed?.Invoke();

    public Task InitializeAsync() { InitCount++; EntitlementKnown = true; return Task.CompletedTask; }
    public Task RefreshAsync() { RefreshCount++; return Task.CompletedTask; }
    public Task RefreshOffersAsync() { RefreshOffersCount++; return Task.CompletedTask; }

    public Task<PurchaseOutcome> PurchaseAsync(SubscriptionPlan plan)
    {
        if (PurchaseResult == PurchaseOutcome.Success)
            HasActiveEntitlement = true;
        return Task.FromResult(PurchaseResult);
    }

    public Task<PurchaseOutcome> RestoreAsync()
    {
        if (RestoreResult == PurchaseOutcome.Success)
            HasActiveEntitlement = true;
        return Task.FromResult(RestoreResult);
    }

    public Task<string?> GetManagementUrlAsync() => Task.FromResult(ManagementUrl);
}
