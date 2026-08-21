namespace Animal_Diary_App.Data.ViewModels;

using System.Collections.ObjectModel;
using System.Windows.Input;
using Animal_Diary_App.Data.Services;
using Animal_Diary_App.Data.Services.Analytics;
using Animal_Diary_App.Data.Services.Billing;
using Animal_Diary_App.Data.Services.Journal;
using Animal_Diary_App.Helpers;

/// <summary>
/// The subscribe sheet: the dignified, respectful ask. One <c>FelovaBottomSheet</c>
/// showing the yearly (emphasized) and monthly options from the store, plus Restore.
/// It is never a "SUBSCRIBE NOW to unlock" wall — the copy treats the person as the
/// real user they already are.
///
/// <para>It sells the payoff, never the labour: what it offers is the assembled
/// appointment summary, the designed report, backup, a second pet and caregiver invites.
/// Writing things down is free forever, so this sheet is never what stands between
/// someone and recording what just happened. It is reached from the real upgrade doors
/// only (see <c>AnalyticsEvents.SubscribeSource*</c>), and the <see cref="Open"/> source
/// rides along to analytics so we learn which door people actually convert at.</para>
///
/// <para>All money goes through <see cref="IEntitlementService"/>; this VM only holds
/// sheet state. Under the Null boundary (dev, or before the store is wired) there are
/// no offers and purchase reports unavailable — the sheet still renders its message.</para>
/// </summary>
public sealed class SubscribeSheetViewModel : BaseViewModel, IResettableDraft
{
    private readonly IEntitlementService _entitlements;
    private readonly IAnalyticsService _analytics;
    private readonly HistoryDepthService _history;
    private readonly ActivePetService _activePet;

    private string _source = AnalyticsEvents.SubscribeSourceSettings;

    /// <summary>The history bucket as of this opening. Resolved when the sheet opens so the
    /// purchase event carries the same value the paywall event did — the two are read as
    /// one funnel, and a boundary crossed between them would split a person across two
    /// cohorts. Empty until the first open.</summary>
    private string _historyBucket = AnalyticsHistory.None;

    public SubscribeSheetViewModel(
        IEntitlementService entitlements,
        IAnalyticsService analytics,
        HistoryDepthService history,
        ActivePetService activePet)
    {
        _entitlements = entitlements;
        _analytics = analytics;
        _history = history;
        _activePet = activePet;

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

    /// <summary>Full access from a redeemed access code. Shares the quiet "you're covered"
    /// face with <see cref="IsSubscribed"/> for the same reason — re-pitching a sale to
    /// someone who already has access is the thing that face exists to prevent — but says
    /// something different on it: they did not buy anything, so the copy names the end date
    /// and never thanks them for subscribing.</summary>
    public bool IsGranted => _entitlements.State == AccessState.Granted;

    /// <summary>Which face of the sheet shows.</summary>
    public bool ShowSubscribed => IsSubscribed || IsGranted;
    public bool ShowOffers => !ShowSubscribed;

    /// <summary>Sheet header, mode-aware.</summary>
    public string SheetTitle => Loc(
        IsGranted ? "Subscribe_GrantedTitle"
        : IsSubscribed ? "Subscribe_SubscribedTitle"
        : "Subscribe_Title");

    public string SheetSubtitle =>
        IsGranted ? LocalizationManager.Instance.Format(
            "Subscribe_GrantedSubtitleFormat", FormatGrantEnd(_entitlements.GrantedUntilUtc))
        : IsSubscribed ? string.Empty
        : Loc("Subscribe_Subtitle");

    /// <summary>"Manage subscription" is a store link, and a grant has no store record
    /// behind it. Showing it to a granted user sends them to a Play page about a
    /// subscription they never bought.</summary>
    public bool ShowManage => IsSubscribed;

    private static string FormatGrantEnd(DateTime? untilUtc)
        => untilUtc is DateTime u ? u.ToLocalTime().ToString("d") : string.Empty;

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
        IsPresented = true;
        // The sheet is already on screen before this resolves. Deliberate: the history
        // bucket costs a read, and telemetry may never sit between someone and the screen
        // they asked for.
        TrackViewedAsync(source).Forget();
        // Re-fetch on open: the initial load may still be in flight or have failed on a
        // flaky launch, which is exactly when the empty-offers dead-end appeared.
        if (ShowOffers)
            LoadOffersAsync().Forget();
    }

    /// <summary>Resolve how deep the record is, then record the view. Non-throwing: a
    /// telemetry property may never be the thing that breaks the paywall.</summary>
    private async Task TrackViewedAsync(string source)
    {
        try
        {
            _historyBucket = await _history.BucketAsync(_activePet.ActivePet);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Analytics] history bucket failed: {ex.Message}");
            _historyBucket = AnalyticsHistory.None;
        }

        _analytics.Track(AnalyticsEvents.SubscribeScreenViewed, new Dictionary<string, object?>
        {
            [AnalyticsEvents.PropSubscribeSource] = source,
            [AnalyticsEvents.PropDaysOfHistory] = _historyBucket,
        });
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
        OnPropertyChanged(nameof(IsGranted));
        OnPropertyChanged(nameof(ShowSubscribed));
        OnPropertyChanged(nameof(ShowOffers));
        OnPropertyChanged(nameof(ShowManage));
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
                        // The value this sheet's opening resolved, not a fresh read: the two
                        // events are one funnel and must land in the same cohort.
                        [AnalyticsEvents.PropDaysOfHistory] = _historyBucket,
                    });
                    // Don't just vanish — flip to the thank-you / subscribed view so the
                    // purchase is confirmed. RefreshMode also runs via StateChanged, but
                    // call it directly so the transition is immediate.
                    StatusText = string.Empty;
                    RefreshMode();
                    break;
                case PurchaseOutcome.AlreadySubscribed:
                    // Nothing was bought — the store account already had it and it belongs to
                    // this account. Access is on, so flip to the subscribed view, but never
                    // claim a purchase just happened: that reads as a second charge.
                    StatusText = Loc("Subscribe_AlreadySubscribed");
                    RefreshMode();
                    break;
                case PurchaseOutcome.OwnedByAnotherAccount:
                    // The store won't sell a second subscription and we can't grant this one
                    // here. Name the situation and the two real ways out; "try again" is not
                    // one of them.
                    StatusText = Loc("Subscribe_OwnedByOtherAccount");
                    _analytics.Track(AnalyticsEvents.PurchaseFailed, new Dictionary<string, object?>
                    {
                        [AnalyticsEvents.PropReason] = AnalyticsEvents.ReasonOwnedByOtherAccount,
                    });
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
            if (outcome is PurchaseOutcome.Success or PurchaseOutcome.AlreadySubscribed)
            {
                // Restored → flip to the subscribed view (its own confirmation).
                StatusText = string.Empty;
                RefreshMode();
            }
            else if (outcome == PurchaseOutcome.OwnedByAnotherAccount)
            {
                // Restore is how most people meet this: the store account's subscription is
                // held by a different Felova account, so there is nothing to restore HERE.
                StatusText = Loc("Subscribe_OwnedByOtherAccount");
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
