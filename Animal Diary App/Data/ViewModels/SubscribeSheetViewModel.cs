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
        ManageCommand = new Command(async () => await OpenManageAsync());
        RetryCommand = new Command(async () => await LoadOffersAsync());

        // The entitlement can change under the sheet (a restore completes, a purchase
        // lands) — reflect it. Marshalled to the UI thread by the raiser's callers.
        _entitlements.StateChanged += () => MainThread.BeginInvokeOnMainThread(RefreshMode);
    }

    // ── sheet shell ───────────────────────────────────────────────────────────

    private bool _isPresented;
    public bool IsPresented
    {
        get => _isPresented;
        set => SetProperty(ref _isPresented, value);
    }

    /// <summary>True once a subscription is active — the sheet then shows the quiet
    /// thank-you / subscribed view instead of the purchase offers (so re-opening it from
    /// Settings, or landing here right after buying, never re-pitches the sale).</summary>
    public bool IsSubscribed => _entitlements.State == AccessState.Subscribed;

    /// <summary>Which face of the sheet shows.</summary>
    public bool ShowSubscribed => IsSubscribed;
    public bool ShowOffers => !IsSubscribed;

    /// <summary>Sheet header, mode-aware.</summary>
    public string SheetTitle => Loc(IsSubscribed ? "Subscribe_SubscribedTitle" : "Subscribe_Title");
    public string SheetSubtitle => IsSubscribed ? string.Empty : Loc("Subscribe_Subtitle");

    /// <summary>The offers, yearly first, each with a store-localized price. Empty until
    /// the store loads / under the Null boundary.</summary>
    public ObservableCollection<SubscriptionOfferItem> Offers { get; } = new();

    private bool _isLoadingOffers;
    /// <summary>True while the offerings are being (re)fetched on open — shows a spinner
    /// instead of prematurely deciding the offers are missing.</summary>
    public bool IsLoadingOffers
    {
        get => _isLoadingOffers;
        private set
        {
            if (SetProperty(ref _isLoadingOffers, value))
            {
                OnPropertyChanged(nameof(ShowOfferList));
                OnPropertyChanged(nameof(ShowLoadingOffers));
                OnPropertyChanged(nameof(ShowOffersProblem));
            }
        }
    }

    /// <summary>Have offers, done loading → show the purchase buttons.</summary>
    public bool ShowOfferList => ShowOffers && !IsLoadingOffers && Offers.Count > 0;

    /// <summary>Fetching → show the spinner.</summary>
    public bool ShowLoadingOffers => ShowOffers && IsLoadingOffers;

    /// <summary>Done loading with nothing to show → the problem message + Try again.</summary>
    public bool ShowOffersProblem => ShowOffers && !IsLoadingOffers && Offers.Count == 0;

    /// <summary>The problem message distinguishes the two causes a user can act on:
    /// offline (check your connection) vs. everything else (our end / store hiccup).</summary>
    public string OffersProblemMessage => Loc(IsOffline ? "Subscribe_Offline" : "Subscribe_NoOffers");

    private static bool IsOffline =>
        Microsoft.Maui.Networking.Connectivity.Current.NetworkAccess != Microsoft.Maui.Networking.NetworkAccess.Internet;

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

    /// <summary>Subscribed view: open the store's subscription management page (change /
    /// cancel). Android only for now.</summary>
    public ICommand ManageCommand { get; }

    /// <summary>Re-fetch the offerings after a load failure (the "Try again" link).</summary>
    public ICommand RetryCommand { get; }

    /// <summary>Open the sheet, recording where it was opened from. <paramref name="source"/>
    /// is one of the <c>AnalyticsEvents.SubscribeSource*</c> constants.</summary>
    public void Open(string source)
    {
        _source = source;
        StatusText = string.Empty;
        RefreshMode();
        _analytics.Track(AnalyticsEvents.SubscribeScreenViewed, new Dictionary<string, object?>
        {
            [AnalyticsEvents.PropSubscribeSource] = source,
        });
        IsPresented = true;
        // Re-fetch on open: the initial load may still be in flight or have failed on a
        // flaky launch, which is exactly when the empty-offers dead-end appeared.
        if (ShowOffers)
            _ = LoadOffersAsync();
    }

    /// <summary>(Re)fetch the offerings with a visible loading state, then reflect the
    /// result. Non-throwing.</summary>
    private async Task LoadOffersAsync()
    {
        if (IsLoadingOffers)
            return;
        IsLoadingOffers = true;
        try
        {
            await _entitlements.RefreshOffersAsync();
        }
        finally
        {
            IsLoadingOffers = false;
            RefreshMode();
        }

        // Production visibility (debug logs aren't in the field): record when the sheet
        // ends up with nothing to show, split by cause.
        if (ShowOffersProblem)
            _analytics.Track(AnalyticsEvents.OffersLoadFailed, new Dictionary<string, object?>
            {
                [AnalyticsEvents.PropReason] = IsOffline ? AnalyticsEvents.ReasonOffline : AnalyticsEvents.ReasonEmpty,
            });
    }

    /// <summary>Rebuild both faces of the sheet: the offer list and the subscribed/offers
    /// switch. Called on open and whenever the entitlement changes underneath it.</summary>
    private void RefreshMode()
    {
        Offers.Clear();
        // Yearly first (the emphasized/default option); highlight whichever is yearly.
        foreach (var offer in _entitlements.Offers.OrderBy(o => o.Plan == SubscriptionPlan.Yearly ? 0 : 1))
            Offers.Add(new SubscriptionOfferItem(offer, isBest: offer.Plan == SubscriptionPlan.Yearly));

        OnPropertyChanged(nameof(ShowOfferList));
        OnPropertyChanged(nameof(ShowLoadingOffers));
        OnPropertyChanged(nameof(ShowOffersProblem));
        OnPropertyChanged(nameof(OffersProblemMessage));
        OnPropertyChanged(nameof(IsSubscribed));
        OnPropertyChanged(nameof(ShowSubscribed));
        OnPropertyChanged(nameof(ShowOffers));
        OnPropertyChanged(nameof(SheetTitle));
        OnPropertyChanged(nameof(SheetSubtitle));
    }

    private async Task OpenManageAsync()
    {
        // Prefer the store's per-platform management URL (deep-links to this subscription);
        // fall back to the generic Play subscriptions page.
        var url = await _entitlements.GetManagementUrlAsync()
                  ?? "https://play.google.com/store/account/subscriptions";
        try { await Launcher.OpenAsync(url); }
        catch { /* no store app / cancelled — nothing to do */ }
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
                    // Don't just vanish — flip to the thank-you / subscribed view so the
                    // purchase is confirmed. RefreshMode also runs via StateChanged, but
                    // call it directly so the transition is immediate.
                    StatusText = string.Empty;
                    RefreshMode();
                    break;
                case PurchaseOutcome.Cancelled:
                    // The user backed out of the store sheet — not an error, say nothing.
                    break;
                case PurchaseOutcome.Pending:
                    // Deferred payment / entitlement not surfaced yet — NOT a failure. Say
                    // it's processing and leave it; a resume/refresh will unlock it.
                    StatusText = Loc("Subscribe_PurchasePending");
                    break;
                default: // Failed / Unavailable
                    StatusText = Loc("Subscribe_PurchaseProblem");
                    _analytics.Track(AnalyticsEvents.PurchaseFailed, new Dictionary<string, object?>
                    {
                        [AnalyticsEvents.PropReason] = outcome == PurchaseOutcome.Unavailable
                            ? AnalyticsEvents.ReasonUnavailable : AnalyticsEvents.ReasonFailed,
                    });
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
            if (outcome == PurchaseOutcome.Success)
            {
                // Restored → flip to the subscribed view (its own confirmation).
                StatusText = string.Empty;
                RefreshMode();
            }
            else if (outcome == PurchaseOutcome.NothingToRestore)
            {
                StatusText = Loc("Subscribe_RestoreNone");
            }
            else
            {
                // Distinguish offline (actionable) from a generic problem, like the offers path.
                StatusText = Loc(IsOffline ? "Subscribe_Offline" : "Subscribe_RestoreProblem");
                _analytics.Track(AnalyticsEvents.RestoreFailed, new Dictionary<string, object?>
                {
                    [AnalyticsEvents.PropReason] = IsOffline ? AnalyticsEvents.ReasonOffline : AnalyticsEvents.ReasonFailed,
                });
            }
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
