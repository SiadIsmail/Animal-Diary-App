namespace Animal_Diary_App.Data.ViewModels;

using System.Diagnostics;
using System.Windows.Input;
using Animal_Diary_App.Data.Services.Analytics;
using Animal_Diary_App.Data.Services.Billing;
using Animal_Diary_App.Data.Services.Cloud;
using Animal_Diary_App.Helpers;

/// <summary>
/// Redeem an access code: one field, one button, one sentence back.
///
/// <para><b>Signed out is the normal case, not an edge case.</b> A code is handed to someone
/// who may have installed the app minutes ago, so the sheet's first job when there is no
/// account is to say a code lives on an account and hand off to the account door, rather than
/// failing a redemption they cannot fix.</para>
///
/// <para>The success line names the END DATE, never an amount of time added. A second code
/// renews from the moment it is redeemed rather than stacking (see migration 0015), so "you
/// added a year" is false for anyone who redeems early and "you now have 18 months" is false
/// for everyone. One date is true in every case.</para>
/// </summary>
public sealed class RedeemCodeSheetViewModel : BaseViewModel, IResettableDraft
{
    private readonly ICloudAccessCodeService _codes;
    private readonly ICloudAuthService _auth;
    private readonly IEntitlementService _entitlements;
    private readonly IAnalyticsService _analytics;
    private readonly CloudSheetViewModel _cloud;

    public RedeemCodeSheetViewModel(
        ICloudAccessCodeService codes,
        ICloudAuthService auth,
        IEntitlementService entitlements,
        IAnalyticsService analytics,
        CloudSheetViewModel cloud)
    {
        _codes = codes;
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
        IsBusy = false;
        RefreshMode();
        IsPresented = true;
    }

    // ── mode ────────────────────────────────────────────────────────────────

    /// <summary>Signed in ⇒ the code field. Signed out ⇒ the "a code lives on an account"
    /// hand-off. Read once per open (and after a sign-in returns) rather than bound live, so
    /// the sheet cannot swap its body under someone mid-type.</summary>
    public bool IsSignedIn { get; private set; }

    public bool ShowForm => IsSignedIn;
    public bool ShowSignInPrompt => !IsSignedIn;

    private void RefreshMode()
    {
        IsSignedIn = _auth.IsSignedIn;
        OnPropertyChanged(nameof(IsSignedIn));
        OnPropertyChanged(nameof(ShowForm));
        OnPropertyChanged(nameof(ShowSignInPrompt));
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
        IsBusy = true;
        try
        {
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
    /// (<c>access_code_stats</c>), where it is exact — see AI/analytics.md.</summary>
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

    /// <summary>A global data reset clears the half-typed code; this VM is a singleton, so
    /// the draft would otherwise survive it.</summary>
    public void ResetDraft()
    {
        Code = string.Empty;
        ErrorText = string.Empty;
        SuccessText = string.Empty;
        IsPresented = false;
    }
}
