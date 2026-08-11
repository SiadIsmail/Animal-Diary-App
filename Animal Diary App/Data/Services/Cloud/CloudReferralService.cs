namespace Animal_Diary_App.Data.Services.Cloud;

using System.Diagnostics;
using System.Text.Json;
using Animal_Diary_App.Data.Services.Data;
using Animal_Diary_App.Helpers;

/// <summary>Entering a creator's code. Attribution only: a creator code grants nothing.</summary>
public interface ICloudReferralService
{
    /// <summary>Try the typed text as a creator code. Returns the creator's display name when
    /// it is one, or <c>null</c> when it is not (the caller then tries the access-code path).
    ///
    /// <para>Works <b>signed out</b>, which is the point: the person typing "THETO" has
    /// usually just installed the app. Signed out it tags the store identity and remembers
    /// the code on the device; signed in it also records it against the account.</para>
    ///
    /// <para>Never throws for an unknown code — this is a lookup on a shared input box, not
    /// a failed redemption. A network failure does throw <see cref="CloudException"/>.</para></summary>
    Task<string?> TryEnterAsync(string code);

    /// <summary>Push a code entered before there was an account onto the account, once there
    /// is one. Idempotent and non-throwing; called on sign-in and at launch.</summary>
    Task ClaimPendingAsync();
}

/// <summary>
/// Creator-code attribution, both halves of it.
///
/// <para><b>Why the device remembers the code in <c>AppSettings</c>, not under the
/// <c>cloud:</c> prefix</b> — the opposite of where <see cref="CloudAccessCodeService"/>
/// keeps a grant, and deliberately so. A grant is <i>access</i>, so tying it to the account
/// and dropping it on sign-out is what stops one code covering unlimited accounts. A creator
/// code is a <i>marketing tag</i> that grants nothing: there is nothing to abuse by keeping
/// it, and the person who typed it almost certainly had no account yet, so dropping it on
/// sign-out would throw away the attribution the code exists to produce.</para>
///
/// <para>The two halves cover each other's gap. The store attribute reaches anonymous
/// buyers but lives with a third party and dies with a reinstall; the account record is
/// exact and ours but only exists after sign-in. Neither is complete alone.</para>
/// </summary>
public sealed class CloudReferralService : ICloudReferralService
{
    // Device-scoped (AppSettings), so it survives sign-out and predates any account.
    private const string KeyCode = "ReferralCode";
    private const string KeyCreator = "ReferralCreator";
    private const string KeyClaimed = "ReferralClaimed";

    private readonly CloudHttp _http;
    private readonly ICloudAuthService _auth;
    private readonly SettingsService _settings;
    private readonly Billing.IEntitlementService _entitlements;

    public CloudReferralService(
        CloudHttp http,
        ICloudAuthService auth,
        SettingsService settings,
        Billing.IEntitlementService entitlements)
    {
        _http = http;
        _auth = auth;
        _settings = settings;
        _entitlements = entitlements;

        // Signing in is the moment a code typed by an anonymous installer can finally be
        // attached to an account. Fire-and-forget: nothing downstream waits on attribution.
        _auth.SessionChanged += () =>
        {
            if (_auth.IsSignedIn)
                Task.Run(ClaimPendingAsync).Forget();
        };
    }

    public async Task<string?> TryEnterAsync(string code)
    {
        if (!CloudConfig.Enabled)
            return null;

        var normalized = (code ?? string.Empty).Trim().ToUpperInvariant();
        if (normalized.Length == 0)
            return null;

        var session = await _auth.GetSessionAsync();

        // Signed in: record the entry AND move the account's last-touch pointer, in one call.
        // Signed out: the anonymous lookup (granted to `anon` in migration 0016) still tells
        // us whether this is a creator code, which is what lets someone with no account enter
        // one at all.
        var creator = session != null
            ? await CallAsync("enter_creator_code", normalized, session.AccessToken)
            : await CallAsync("lookup_creator_code", normalized, accessToken: null);

        if (creator == null)
            return null;

        await RememberAsync(normalized, creator, claimed: session != null);
        return creator;
    }

    public async Task ClaimPendingAsync()
    {
        if (!CloudConfig.Enabled || !_auth.IsSignedIn)
            return;

        try
        {
            if (await _settings.GetFlagAsync(KeyClaimed))
                return;

            var code = await _settings.GetValueAsync(KeyCode);
            if (string.IsNullOrEmpty(code))
                return;

            var session = await _auth.GetSessionAsync();
            if (session == null)
                return;

            var creator = await CallAsync("enter_creator_code", code, session.AccessToken);
            if (creator == null)
            {
                // The code was retired or renamed between typing and signing in. Mark it
                // claimed anyway: retrying forever on every launch cannot make it valid.
                Debug.WriteLine($"[Referral] pending code '{code}' is no longer a creator code");
                await _settings.SetFlagAsync(KeyClaimed, true);
                return;
            }

            await RememberAsync(code, creator, claimed: true);
            Debug.WriteLine($"[Referral] claimed pending code for {creator}");
        }
        catch (Exception ex)
        {
            // Offline at sign-in is normal. Leaving the flag unset means the next launch or
            // the next sign-in tries again.
            Debug.WriteLine($"[Referral] claim failed: {ex.Message}");
        }
    }

    /// <summary>Both RPCs return a bare creator name or SQL null; the difference is only
    /// whether the call is authenticated.</summary>
    private async Task<string?> CallAsync(string function, string code, string? accessToken)
    {
        var doc = await _http.RpcAsync(function, new { p_code = code }, accessToken ?? string.Empty);
        if (doc == null || doc.RootElement.ValueKind != JsonValueKind.String)
            return null;
        var creator = doc.RootElement.GetString();
        return string.IsNullOrWhiteSpace(creator) ? null : creator;
    }

    private async Task RememberAsync(string code, string creator, bool claimed)
    {
        await _settings.SetValueAsync(KeyCode, code);
        await _settings.SetValueAsync(KeyCreator, creator);
        await _settings.SetFlagAsync(KeyClaimed, claimed);

        // The half that reaches anonymous buyers. Done last and independently of the flag
        // above, so it is re-applied on a later claim too — RevenueCat's identity can change
        // under us (an anonymous install that later logs in), and re-setting is idempotent.
        await _entitlements.SetAttributionAsync(code);
    }
}

/// <summary>Registered when the cloud is switched off. No lookup is possible, so no text is
/// ever a creator code and the caller falls straight through to the access-code path.</summary>
public sealed class NullCloudReferralService : ICloudReferralService
{
    public Task<string?> TryEnterAsync(string code) => Task.FromResult<string?>(null);
    public Task ClaimPendingAsync() => Task.CompletedTask;
}
