namespace Animal_Diary_App.Data.ViewModels;

using System.Windows.Input;
using Animal_Diary_App.Data.Services.Analytics;
using Animal_Diary_App.Helpers;

/// <summary>
/// The three quiet, reassuring trial moments, all on one reusable sheet (leaner for a
/// solo maintainer than three near-identical screens):
/// <list type="bullet">
///   <item><b>Explainer</b> — after the first real log. Leads with the gift; the
///   subscribe ask is a single low-key line, never a button.</item>
///   <item><b>Pre-end nudge</b> — once, a few days out. Loss aversion anchored to what
///   the owner has actually built (real dose + history counts), not a countdown.</item>
///   <item><b>Read-only reassurance</b> — once, when the care-only state begins.
///   Reassures first ("{pet}'s history is safe and always here"), then offers to
///   continue.</item>
/// </list>
/// Copy lives in resources; this VM only selects which lines and whether the "see
/// options" button shows. "Continue with Felova" opens <see cref="SubscribeSheetViewModel"/>
/// — never a "subscribe now to unlock" wall.
/// </summary>
public sealed class TrialMessageViewModel : BaseViewModel
{
    private readonly SubscribeSheetViewModel _subscribe;
    private readonly IAnalyticsService _analytics;

    private string _subscribeSource = AnalyticsEvents.SubscribeSourceExplainer;

    public TrialMessageViewModel(SubscribeSheetViewModel subscribe, IAnalyticsService analytics)
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

    /// <summary>The single low-key subscribe line under the explainer body ("You can
    /// subscribe anytime in Settings"). Empty on the other modes, which use the button.</summary>
    public string Footnote { get; private set; } = string.Empty;
    public bool HasFootnote => !string.IsNullOrEmpty(Footnote);

    /// <summary>Whether the "Continue with Felova" (→ subscribe) button shows. The
    /// explainer deliberately hides it; the nudge and read-only states show it.</summary>
    public bool ShowContinue { get; private set; }

    /// <summary>Primary (→ subscribe) button label. Never "subscribe now to unlock".</summary>
    public string ContinueLabel => Loc("Subscribe_ContinueLabel");

    /// <summary>Secondary/dismiss button label ("Not now" / "Okay").</summary>
    public string DismissLabel { get; private set; } = string.Empty;

    public ICommand DismissCommand { get; }
    public ICommand ContinueCommand { get; }

    /// <summary>After the first log: reassuring explainer, no prominent ask.</summary>
    public void ShowExplainer(string petName)
    {
        _subscribeSource = AnalyticsEvents.SubscribeSourceExplainer;
        // Title carries the trial length (config-driven), body carries the pet name.
        var days = (int)Billing_TrialDays();
        Title = LocalizationManager.Instance.Format("Subscribe_ExplainerTitle", days);
        Body = Fmt("Subscribe_ExplainerBody", petName);
        Footnote = Loc("Subscribe_ExplainerFootnote");
        ShowContinue = false;
        DismissLabel = Loc("Common_Okay");
        Present();
        _analytics.Track(AnalyticsEvents.TrialExplainerShown);
    }

    /// <summary>A few days before the trial ends: anchored to what they built.</summary>
    public void ShowNudge(string petName, int daysLeft, int doseCount, int weeksTracked)
    {
        _subscribeSource = AnalyticsEvents.SubscribeSourceNudge;
        Title = Fmt("Subscribe_NudgeTitle", petName);
        // Body references the real accumulated record; the caller passes the counts.
        Body = LocalizationManager.Instance.Format(
            "Subscribe_NudgeBody", doseCount, weeksTracked, petName);
        Footnote = string.Empty;
        ShowContinue = true;
        DismissLabel = Loc("Common_NotNow");
        Present();
        _analytics.Track(AnalyticsEvents.PreEndNudgeShown, new Dictionary<string, object?>
        {
            [AnalyticsEvents.PropTrialDay] = Math.Max(0, (int)Billing_TrialDays() - daysLeft),
        });
    }

    /// <summary>
    /// A caregiver's cover ended — the owner's subscription lapsed or their trial ran out.
    /// A separate mode from <see cref="ShowReadOnly"/> because this person never had a
    /// trial of their own (the clock starts with your first OWN pet), so the read-only copy
    /// would tell them something untrue. States the real reason, does not blame the owner,
    /// and still offers a subscription of their own as the way to carry on.
    /// </summary>
    /// <param name="petName">The affected pet, but only when it is the one on screen;
    /// empty selects the general wording rather than naming an off-screen pet.</param>
    public void ShowSponsorshipEnded(string petName)
    {
        _subscribeSource = AnalyticsEvents.SubscribeSourceSponsorshipEnded;
        var named = !string.IsNullOrEmpty(petName);
        Title = named ? Fmt("Subscribe_SponsorEndedTitle", petName) : Loc("Subscribe_SponsorEndedTitleGeneric");
        Body = named ? Fmt("Subscribe_SponsorEndedBody", petName) : Loc("Subscribe_SponsorEndedBodyGeneric");
        Footnote = string.Empty;
        ShowContinue = true;
        DismissLabel = Loc("Common_Okay");
        Present();
        _analytics.Track(AnalyticsEvents.SponsorshipEnded);
    }

    /// <summary>
    /// A redeemed access code's year is nearly up. The grant sibling of
    /// <see cref="ShowNudge"/>, and separate for the same reason
    /// <see cref="ShowSponsorshipEnded"/> is separate: the trial copy names a free trial
    /// and a trial length, neither of which is true here.
    /// </summary>
    /// <param name="endsOn">Local end date, already formatted by the caller.</param>
    public void ShowGrantEnding(string petName, string endsOn)
    {
        _subscribeSource = AnalyticsEvents.SubscribeSourceNudge;
        Title = Fmt("Subscribe_GrantEndingTitle", petName);
        Body = LocalizationManager.Instance.Format("Subscribe_GrantEndingBody", petName, endsOn);
        Footnote = string.Empty;
        ShowContinue = true;
        DismissLabel = Loc("Common_NotNow");
        Present();
        _analytics.Track(AnalyticsEvents.GrantEndingShown);
    }

    /// <summary>
    /// The care-only state begins after a GRANT ran out, not a trial. Same reassurance,
    /// different truth: this person redeemed a code rather than starting a 14-day trial,
    /// so <see cref="ShowReadOnly"/>'s wording would date their access wrongly by a year.
    /// </summary>
    public void ShowGrantEnded(string petName, bool hasCaregivers = false)
    {
        _subscribeSource = AnalyticsEvents.SubscribeSourceReadOnly;
        Title = Fmt("Subscribe_GrantEndedTitle", petName);
        Body = Fmt("Subscribe_GrantEndedBody", petName)
             + (hasCaregivers ? " " + Fmt("Subscribe_ReadOnlyCarersClause", petName) : string.Empty);
        Footnote = string.Empty;
        ShowContinue = true;
        DismissLabel = Loc("Common_NotNow");
        Present();
        _analytics.Track(AnalyticsEvents.GrantEnded);
    }

    /// <summary>The care-only state begins: reassure first, then offer to continue.</summary>
    /// <param name="hasCaregivers">Whether this owner shares any pet. When they do, the
    /// copy adds that their carers can now only view too — they are the only person who can
    /// change that, and finding out by watching someone else fail to log a dose is worse.</param>
    public void ShowReadOnly(string petName, int trialDayReached, bool hasCaregivers = false)
    {
        _subscribeSource = AnalyticsEvents.SubscribeSourceReadOnly;
        Title = Fmt("Subscribe_ReadOnlyTitle", petName);
        Body = Fmt("Subscribe_ReadOnlyBody", petName)
             + (hasCaregivers ? " " + Fmt("Subscribe_ReadOnlyCarersClause", petName) : string.Empty);
        Footnote = string.Empty;
        ShowContinue = true;
        DismissLabel = Loc("Common_NotNow");
        Present();
        _analytics.Track(AnalyticsEvents.ReadOnlyEntered, new Dictionary<string, object?>
        {
            [AnalyticsEvents.PropTrialDay] = trialDayReached,
        });
    }

    private void Present()
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Body));
        OnPropertyChanged(nameof(Footnote));
        OnPropertyChanged(nameof(HasFootnote));
        OnPropertyChanged(nameof(ShowContinue));
        OnPropertyChanged(nameof(DismissLabel));
        IsPresented = true;
    }

    private static double Billing_TrialDays() =>
        Animal_Diary_App.Data.Services.Billing.BillingConfig.TrialLength.TotalDays;

    private static string Loc(string key) => LocalizationManager.Instance.GetString(key);
    private static string Fmt(string key, string arg) => LocalizationManager.Instance.Format(key, arg);
}
