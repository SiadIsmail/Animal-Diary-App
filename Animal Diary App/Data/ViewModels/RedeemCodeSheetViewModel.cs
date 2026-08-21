namespace Animal_Diary_App.Data.ViewModels;

using System.Diagnostics;
using System.Windows.Input;
using Animal_Diary_App.Data.Services.Analytics;
using Animal_Diary_App.Data.Services.Billing;
using Animal_Diary_App.Data.Services.Cloud;
using Animal_Diary_App.Helpers;

/// <summary>
/// One box, two kinds of code, one sentence back.
///
/// <para><b>The user is never asked which kind they hold</b>, because they do not know. A
/// creator code ("THETO") is public, reusable and grants nothing; an access code
/// (FELOVA-XXXX-XXXX) is unique, single-use and grants a year. The server decides which one
/// was typed. Order matters: the creator lookup runs FIRST because it is cheap and burns
/// none of <c>redeem_access_code</c>'s ten-attempts-per-quarter-hour budget.</para>
///
/// <para><b>Signed out is the normal case, not an edge case</b>, and the two kinds diverge
/// there. A creator code still works with no account (that is the whole point: it must
/// reach someone who installed the app a minute ago and has not signed up). An access code
/// cannot, because a grant has to be bound to an account, so that path hands off to the
/// account door instead of failing a redemption they cannot fix.</para>
///
/// <para>The access-code success line names the END DATE, never an amount of time added. A
/// second code renews from the moment it is redeemed rather than stacking (see migration
/// 0015), so "you added a year" is false for anyone who redeems early and "you now have 18
/// months" is false for everyone. One date is true in every case.</para>
/// </summary>
public sealed class RedeemCodeSheetViewModel : BaseViewModel, IResettableDraft
{
    private readonly ICloudAccessCodeService _codes;
    private readonly ICloudReferralService _referrals;
    private readonly ICloudAuthService _auth;
    private readonly IEntitlementService _entitlements;
    private readonly IAnalyticsService _analytics;
    private readonly CloudSheetViewModel _cloud;

    public RedeemCodeSheetViewModel(
        ICloudAccessCodeService codes,
        ICloudReferralService referrals,
        ICloudAuthService auth,
        IEntitlementService entitlements,
        IAnalyticsService analytics,
        CloudSheetViewModel cloud)
    {
        _codes = codes;
        _referrals = referrals;
        _auth = auth;
        _entitlements = entitlements;
        _analytics = analytics;
        _cloud = cloud;

        DismissCommand = new Command(() => IsPresented = false);
        RedeemCommand = new Command(async () => await RedeemAsync());
        SignInCommand = new Command(() =>
        {
            // The account door replaces this sheet rather than stacking on it: two sheets deep
            // is one Android back press from a confusing half-state, and they come straight
            // back here afterwards knowing the code is still in their hand.
            IsPresented = false;
            _cloud.OpenCommand.Execute(null);
        });
    }

    private bool _isPresented;
    public bool IsPresented
    {
        get => _isPresented;
        set => SetProperty(ref _isPresented, value);
    }

    /// <summary>Open the sheet, clearing whatever the last visit left behind.</summary>
    public void Open()
    {
        Code = string.Empty;
        ErrorText = string.Empty;
        SuccessText = string.Empty;
        NeedsAccount = false;
        IsBusy = false;
        IsPresented = true;
    }

    // ── mode ────────────────────────────────────────────────────────────────

    private bool _needsAccount;
    /// <summary>Set only after a submitted code turned out NOT to be a creator code while
    /// signed out: i.e. the one case where an account is genuinely required.
    ///
    /// <para>This is a <b>result</b> of an attempt, not a mode the sheet opens in. The box is
    /// always shown: gating it on sign-in would block creator codes, which are meant to be
    /// typed by someone who installed the app a minute ago and has no account at all.</para></summary>
    public bool NeedsAccount
    {
        get => _needsAccount;
        private set => SetProperty(ref _needsAccount, value);
    }

    // ── the field ───────────────────────────────────────────────────────────

    private string _code = string.Empty;
    /// <summary>Upper-cased as typed. The server compares upper-cased and trimmed anyway,
    /// but seeing it normalize is what tells someone their lowercase paste was fine.</summary>
    public string Code
    {
        get => _code;
        set
        {
            var normalized = (value ?? string.Empty).ToUpperInvariant();
            if (SetProperty(ref _code, normalized))
                OnPropertyChanged(nameof(CanRedeem));
        }
    }

    /// <summary>Minted codes are FELOVA-XXXX-XXXX, so anything shorter than the prefix plus a
    /// group cannot be one. A cheap guard against burning a rate-limit attempt on a typo, not
    /// a validation rule: the server is the authority on what a code is.</summary>
    public bool CanRedeem => !IsBusy && Code.Trim().Length >= 8;

    private bool _isBusy;
    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(CanRedeem));
                OnPropertyChanged(nameof(CanInteract));
            }
        }
    }

    public bool CanInteract => !IsBusy;

    private string _errorText = string.Empty;
    public string ErrorText { get => _errorText; private set => SetProperty(ref _errorText, value); }

    private string _successText = string.Empty;
    public string SuccessText { get => _successText; private set => SetProperty(ref _successText, value); }

    public ICommand DismissCommand { get; }
    public ICommand RedeemCommand { get; }
    public ICommand SignInCommand { get; }

    // ── redeeming ───────────────────────────────────────────────────────────

    private async Task RedeemAsync()
    {
        if (!CanRedeem)
            return;

        ErrorText = string.Empty;
        SuccessText = string.Empty;
        NeedsAccount = false;
        IsBusy = true;
        try
        {
            // Creator codes first: the lookup is cheap, works signed out, and spends none of
            // redeem_access_code's attempt budget on a code that was never an access code.
            var creator = await _referrals.TryEnterAsync(Code);
            if (creator != null)
            {
                Code = string.Empty;
                SuccessText = LocalizationManager.Instance.Format("Redeem_CreatorThanks", creator);
                _analytics.Track(AnalyticsEvents.CreatorCodeEntered);
                return;
            }

            // Not a creator code. An access code is the only other thing it can be, and that
            // one genuinely needs an account.
            if (!_auth.IsSignedIn)
            {
                NeedsAccount = true;
                Track(OutcomeNeedsAccount);
                return;
            }

            var until = await _codes.RedeemAsync(Code);

            Code = string.Empty;
            SuccessText = LocalizationManager.Instance.Format(
                "Redeem_Success", until.ToLocalTime().ToString("d"));
            Track(OutcomeRedeemed);

            // The gate reads the grant synchronously from the cache the redeem just wrote, but
            // nothing has told the open surfaces to re-read it. This is what turns the app
            // writable again without a relaunch.
            await _entitlements.RefreshAsync();
        }
        catch (CloudException ex)
        {
            Debug.WriteLine($"[Redeem] {ex.Kind} ({ex.StatusCode}): {ex.Message}");
            ErrorText = LocalizationManager.Instance.GetString(ex.Kind switch
            {
                CloudErrorKind.AccessCodeInvalid => "Redeem_ErrInvalid",
                CloudErrorKind.AccessCodeUsed => "Redeem_ErrAlreadyUsed",
                CloudErrorKind.RateLimited => "Redeem_ErrTooMany",
                CloudErrorKind.Network => "Cloud_ErrNetwork",
                CloudErrorKind.AuthExpired => "Redeem_ErrSignedOut",
                _ => "Cloud_ErrGeneric",
            });
            Track(ex.Kind switch
            {
                CloudErrorKind.AccessCodeInvalid => OutcomeInvalid,
                CloudErrorKind.AccessCodeUsed => OutcomeAlreadyUsed,
                CloudErrorKind.RateLimited => OutcomeRateLimited,
                CloudErrorKind.Network => OutcomeOffline,
                _ => OutcomeFailed,
            });
        }
        catch (Exception ex)
        {
            // Never let an unexpected failure escape into an async void handler upstream.
            Debug.WriteLine($"[Redeem] unexpected: {ex.Message}");
            ErrorText = LocalizationManager.Instance.GetString("Cloud_ErrGeneric");
            Track(OutcomeFailed);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Product friction only: are people mistyping codes, hitting the limit, or
    /// bouncing off the account requirement? The code, the campaign and the resulting expiry
    /// are never properties. Campaign attribution is answered in Postgres
    /// (<c>access_code_stats</c>), where it is exact: see AI/analytics.md.</summary>
    private void Track(string outcome) =>
        _analytics.Track(AnalyticsEvents.AccessCodeRedeemed, new Dictionary<string, object?>
        {
            [AnalyticsEvents.PropOutcome] = outcome,
        });

    private const string OutcomeRedeemed = "redeemed";
    private const string OutcomeInvalid = "invalid";
    private const string OutcomeAlreadyUsed = "already_used";
    private const string OutcomeRateLimited = "rate_limited";
    private const string OutcomeOffline = "offline";
    private const string OutcomeFailed = "failed";
    private const string OutcomeNeedsAccount = "needs_account";

    /// <summary>A global data reset clears the half-typed code; this VM is a singleton, so
    /// the draft would otherwise survive it.</summary>
    public void ResetDraft()
    {
        Code = string.Empty;
        ErrorText = string.Empty;
        SuccessText = string.Empty;
        NeedsAccount = false;
        IsPresented = false;
    }
}
