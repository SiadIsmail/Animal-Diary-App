namespace Animal_Diary_App.Data.ViewModels;

using System.Collections.ObjectModel;
using System.Windows.Input;
using Animal_Diary_App.Data.Services.Analytics;
using Animal_Diary_App.Data.Services.Billing;
using Animal_Diary_App.Helpers;

/// <summary>
/// The subscribe sheet: the dignified, respectful ask. One <c>FelovaBottomSheet</c>
/// showing the yearly (emphasized) and monthly options from the store, plus Restore.
/// It is never a "SUBSCRIBE NOW to unlock" wall — the copy treats the person as the
/// real user they already are. Reached from Settings, the pre-end nudge, the read-only
/// state, and the trial explainer; the <see cref="Open"/> source rides along to
/// analytics so we learn where people actually convert.
///
/// <para>All money goes through <see cref="IEntitlementService"/>; this VM only holds
/// sheet state. Under the Null boundary (dev, or before the store is wired) there are
/// no offers and purchase reports unavailable — the sheet still renders its message.</para>
/// </summary>
public sealed class SubscribeSheetViewModel : BaseViewModel, IResettableDraft
{
    private readonly IEntitlementService _entitlements;
    private readonly IAnalyticsService _analytics;

    private string _source = AnalyticsEvents.SubscribeSourceSettings;

    public SubscribeSheetViewModel(IEntitlementService entitlements, IAnalyticsService analytics)
    {
        _entitlements = entitlements;
        _analytics = analytics;

        DismissCommand = new Command(() => IsPresented = false);
        PurchaseCommand = new Command<SubscriptionOfferItem>(async o => await PurchaseAsync(o));
        RestoreCommand = new Command(async () => await RestoreAsync());

        // The entitlement can change under the sheet (a restore completes, a purchase
        // lands) — reflect it. Marshalled to the UI thread by the raiser's callers.
        _entitlements.StateChanged += () => MainThread.BeginInvokeOnMainThread(RefreshOffers);
    }

    // ── sheet shell ───────────────────────────────────────────────────────────

    private bool _isPresented;
    public bool IsPresented
    {
        get => _isPresented;
        set => SetProperty(ref _isPresented, value);
    }

    /// <summary>The offers, yearly first, each with a store-localized price. Empty until
    /// the store loads / under the Null boundary.</summary>
    public ObservableCollection<SubscriptionOfferItem> Offers { get; } = new();

    public bool HasOffers => Offers.Count > 0;

    /// <summary>Shown in place of the buttons when the store has no offers yet
    /// (loading, offline, or not wired) so the sheet is never an empty dead-end.</summary>
    public bool ShowNoOffersNote => Offers.Count == 0;

    private string _statusText = string.Empty;
    /// <summary>Transient line under the buttons — a restore result, or a purchase error.
    /// Never a price and never blame.</summary>
    public string StatusText { get => _statusText; private set => SetProperty(ref _statusText, value); }

    private bool _isBusy;
    public bool IsBusy
    {
        get => _isBusy;
        private set { if (SetProperty(ref _isBusy, value)) OnPropertyChanged(nameof(CanInteract)); }
    }

    public bool CanInteract => !IsBusy;

    // ── commands ──────────────────────────────────────────────────────────────

    public ICommand DismissCommand { get; }
    public ICommand PurchaseCommand { get; }
    public ICommand RestoreCommand { get; }

    /// <summary>Open the sheet, recording where it was opened from. <paramref name="source"/>
    /// is one of the <c>AnalyticsEvents.SubscribeSource*</c> constants.</summary>
    public void Open(string source)
    {
        _source = source;
        StatusText = string.Empty;
        RefreshOffers();
        _analytics.Track(AnalyticsEvents.SubscribeScreenViewed, new Dictionary<string, object?>
        {
            [AnalyticsEvents.PropSubscribeSource] = source,
        });
        IsPresented = true;
    }

    private void RefreshOffers()
    {
        Offers.Clear();
        // Yearly first (the emphasized/default option); highlight whichever is yearly.
        foreach (var offer in _entitlements.Offers.OrderBy(o => o.Plan == SubscriptionPlan.Yearly ? 0 : 1))
            Offers.Add(new SubscriptionOfferItem(offer, isBest: offer.Plan == SubscriptionPlan.Yearly));

        OnPropertyChanged(nameof(HasOffers));
        OnPropertyChanged(nameof(ShowNoOffersNote));
    }

    private async Task PurchaseAsync(SubscriptionOfferItem? item)
    {
        if (item is null || IsBusy)
            return;

        IsBusy = true;
        StatusText = string.Empty;
        try
        {
            var outcome = await _entitlements.PurchaseAsync(item.Offer.Plan);
            switch (outcome)
            {
                case PurchaseOutcome.Success:
                    _analytics.Track(AnalyticsEvents.SubscriptionPurchased, new Dictionary<string, object?>
                    {
                        [AnalyticsEvents.PropPlan] = item.Offer.Plan == SubscriptionPlan.Yearly
                            ? AnalyticsEvents.PlanYearly : AnalyticsEvents.PlanMonthly,
                        [AnalyticsEvents.PropPrice] = item.Offer.PriceLabel,
                        [AnalyticsEvents.PropSubscribeSource] = _source,
                    });
                    IsPresented = false;
                    break;
                case PurchaseOutcome.Cancelled:
                    // The user backed out of the store sheet — not an error, say nothing.
                    break;
                default:
                    StatusText = Loc("Subscribe_PurchaseProblem");
                    break;
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RestoreAsync()
    {
        if (IsBusy)
            return;

        IsBusy = true;
        StatusText = string.Empty;
        try
        {
            var outcome = await _entitlements.RestoreAsync();
            StatusText = outcome switch
            {
                PurchaseOutcome.Success => Loc("Subscribe_RestoreDone"),
                PurchaseOutcome.NothingToRestore => Loc("Subscribe_RestoreNone"),
                _ => Loc("Subscribe_RestoreProblem"),
            };
            if (outcome == PurchaseOutcome.Success)
                IsPresented = false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static string Loc(string key) => LocalizationManager.Instance.GetString(key);

    public void ResetDraft()
    {
        IsPresented = false;
        StatusText = string.Empty;
        Offers.Clear();
    }
}

/// <summary>A purchasable offer as the sheet renders it: the store offer plus whether
/// it is the emphasized ("best") option (yearly).</summary>
public sealed class SubscriptionOfferItem
{
    public SubscriptionOfferItem(SubscriptionOffer offer, bool isBest)
    {
        Offer = offer;
        IsBest = isBest;
    }

    public SubscriptionOffer Offer { get; }
    public bool IsBest { get; }

    /// <summary>"Yearly" / "Monthly" label.</summary>
    public string PlanLabel => LocalizationManager.Instance.GetString(
        Offer.Plan == SubscriptionPlan.Yearly ? "Subscribe_PlanYearly" : "Subscribe_PlanMonthly");

    /// <summary>Store-localized recurring price, e.g. "€24.99".</summary>
    public string PriceLabel => Offer.PriceLabel;
}
