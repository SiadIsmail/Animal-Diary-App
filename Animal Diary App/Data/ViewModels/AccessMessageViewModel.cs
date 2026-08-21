namespace Animal_Diary_App.Data.ViewModels;

using System.Windows.Input;
using Animal_Diary_App.Data.Services.Analytics;
using Animal_Diary_App.Helpers;

/// <summary>
/// The quiet access moments, on one reusable sheet. It replaces the old
/// <c>TrialMessageViewModel</c>, whose three modes were all about a trial: the
/// post-first-log explainer, the pre-end nudge, and the read-only reassurance. There is
/// no trial any more and there is no read-only state (logging is free forever) so all
/// three are gone along with the copy that described them.
///
/// <para>What survives is the pair of moments a <b>grant</b> produces, and only a grant:</para>
/// <list type="bullet">
///   <item><b>Grant ending</b>: once, a few days before a redeemed access code's year
///   runs out.</item>
///   <item><b>Grant ended</b>: once, when it has. Reassures first; nothing that was
///   written down is affected, and the free tier still writes.</item>
/// </list>
///
/// <para>Both exist because a grant is <b>not</b> a subscription: nothing was charged,
/// nothing renewed, and there was nothing to cancel, so none of the store's own wording
/// fits and none of it may be borrowed. <c>EverGranted</c> is the guard that selects this
/// copy.</para>
///
/// <para>Copy lives in resources; this VM only selects which lines show. "Continue with
/// Felova" opens <see cref="SubscribeSheetViewModel"/>.</para>
/// </summary>
public sealed class AccessMessageViewModel : BaseViewModel
{
    private readonly SubscribeSheetViewModel _subscribe;
    private readonly IAnalyticsService _analytics;

    private string _subscribeSource = AnalyticsEvents.SubscribeSourceGrantEnding;

    public AccessMessageViewModel(SubscribeSheetViewModel subscribe, IAnalyticsService analytics)
    {
        _subscribe = subscribe;
        _analytics = analytics;

        DismissCommand = new Command(() => IsPresented = false);
        ContinueCommand = new Command(() =>
        {
            IsPresented = false;
            _subscribe.Open(_subscribeSource);
        });
    }

    private bool _isPresented;
    public bool IsPresented { get => _isPresented; set => SetProperty(ref _isPresented, value); }

    public string Title { get; private set; } = string.Empty;
    public string Body { get; private set; } = string.Empty;

    /// <summary>Whether the "Continue with Felova" (→ subscribe) button shows.</summary>
    public bool ShowContinue { get; private set; }

    /// <summary>Primary (→ subscribe) button label. Never "subscribe now to unlock".</summary>
    public string ContinueLabel => Loc("Subscribe_ContinueLabel");

    /// <summary>Secondary/dismiss button label ("Not now" / "Okay").</summary>
    public string DismissLabel { get; private set; } = string.Empty;

    public ICommand DismissCommand { get; }
    public ICommand ContinueCommand { get; }

    /// <summary>A redeemed access code's year is nearly up.</summary>
    /// <param name="endsOn">Local end date, already formatted by the caller.</param>
    public void ShowGrantEnding(string petName, string endsOn)
    {
        _subscribeSource = AnalyticsEvents.SubscribeSourceGrantEnding;
        Title = Fmt("Subscribe_GrantEndingTitle", petName);
        Body = LocalizationManager.Instance.Format("Subscribe_GrantEndingBody", petName, endsOn);
        ShowContinue = true;
        DismissLabel = Loc("Common_NotNow");
        Present();
        _analytics.Track(AnalyticsEvents.GrantEndingShown);
    }

    /// <summary>The code's year is up and the account is back on the free tier. The
    /// reassurance is the point and comes first: nothing is lost, nothing is locked, and
    /// writing things down carries on exactly as before.</summary>
    public void ShowGrantEnded(string petName)
    {
        _subscribeSource = AnalyticsEvents.SubscribeSourceGrantEnding;
        Title = Fmt("Subscribe_GrantEndedTitle", petName);
        Body = Fmt("Subscribe_GrantEndedBody", petName);
        ShowContinue = true;
        DismissLabel = Loc("Common_NotNow");
        Present();
        _analytics.Track(AnalyticsEvents.GrantEnded);
    }

    private void Present()
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Body));
        OnPropertyChanged(nameof(ShowContinue));
        OnPropertyChanged(nameof(DismissLabel));
        IsPresented = true;
    }

    private static string Loc(string key) => LocalizationManager.Instance.GetString(key);
    private static string Fmt(string key, string arg) => LocalizationManager.Instance.Format(key, arg);
}
