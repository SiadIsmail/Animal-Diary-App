namespace Animal_Diary_App.Data.Services.Cloud;

using System.Diagnostics;
using System.Text.Json;
using Animal_Diary_App.Data.Services.Analytics;
using Animal_Diary_App.Data.Services.Data;
using Animal_Diary_App.Data.Services.Data.Device;
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

    /// <summary>Launch entry point, in this order: restore the cached creator into the
    /// analytics context, read Google Play's install referrer <b>once per install</b>, and
    /// claim anything still pending. Idempotent and non-throwing.</summary>
    Task InitializeAsync();
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
    private const string KeySource = "ReferralSource";
    private const string KeyReferrerRead = "InstallReferrerRead";
    private const string KeyPendingReferrer = "InstallReferrerPending";

    // How the code reached us. Mirrors migration 0018's `source` column.
    private const string SourceTyped = "typed";
    private const string SourceInstallReferrer = "install_referrer";

    private readonly CloudHttp _http;
    private readonly ICloudAuthService _auth;
    private readonly SettingsService _settings;
    private readonly Billing.IEntitlementService _entitlements;
    private readonly IInstallReferrerSource _installReferrer;

    public CloudReferralService(
        CloudHttp http,
        ICloudAuthService auth,
        SettingsService settings,
        Billing.IEntitlementService entitlements,
        IInstallReferrerSource installReferrer)
    {
        _http = http;
        _auth = auth;
        _settings = settings;
        _entitlements = entitlements;
        _installReferrer = installReferrer;

        // Signing in is the moment a code typed by an anonymous installer can finally be
        // attached to an account. Fire-and-forget: nothing downstream waits on attribution.
        _auth.SessionChanged += () =>
        {
            if (_auth.IsSignedIn)
                Task.Run(ClaimPendingAsync).Forget();
        };
    }

    public Task<string?> TryEnterAsync(string code) => TryEnterAsync(code, SourceTyped);

    private async Task<string?> TryEnterAsync(string code, string source)
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
            ? await CallAsync("enter_creator_code", normalized, session.AccessToken, source)
            : await CallAsync("lookup_creator_code", normalized, accessToken: null);

        if (creator == null)
            return null;

        await RememberAsync(normalized, creator, claimed: session != null, source);
        return creator;
    }

    // ── the install referrer ────────────────────────────────────────────────

    public async Task InitializeAsync()
    {
        // Restore first and unconditionally: an install that already knows its creator must
        // carry it on every event from the FIRST one of the session, not from whenever a
        // network call happens to finish.
        try
        {
            var cached = await _settings.GetValueAsync(KeyCreator);
            if (!string.IsNullOrEmpty(cached))
                AnalyticsContext.ReferralSource = cached;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Referral] restoring analytics context failed: {ex.Message}");
        }

        await TryInstallReferrerAsync();
        await ClaimPendingAsync();
    }

    /// <summary>Read Google Play's install referrer and, if it names a real creator code,
    /// enter it exactly as a typed code would be.
    ///
    /// <para><b>Reading and registering are two separately-guarded steps, and fusing them was
    /// a bug.</b> The READ is one-shot, flagged before it runs: Google says read once, the
    /// value cannot change short of a reinstall, and a device with an unhealthy Play Store
    /// would otherwise pay the timeout on every launch forever for an answer that is not
    /// coming. But REGISTERING the result needs the network, and first launch is exactly when
    /// a phone is least likely to have it — someone installing on mobile data in a vet's
    /// basement. Flagging both together threw the referral away permanently the moment that
    /// first call failed. The parsed candidate is therefore persisted and retried on later
    /// launches until it lands.</para></summary>
    private async Task TryInstallReferrerAsync()
    {
        try
        {
            var candidate = await _settings.GetValueAsync(KeyPendingReferrer);

            // Step 1: read from Play, once per install.
            if (!await _settings.GetFlagAsync(KeyReferrerRead))
            {
                var raw = await _installReferrer.GetInstallReferrerAsync();
                await _settings.SetFlagAsync(KeyReferrerRead, true);

                candidate = InstallReferrerParser.Parse(raw);
                if (candidate != null)
                    await _settings.SetValueAsync(KeyPendingReferrer, candidate);
            }

            if (string.IsNullOrEmpty(candidate))
                return;

            // Step 2: register it, retried until it succeeds. Validated against the real
            // codes server-side, never trusted — which is what makes Play's organic default
            // (utm_source=google-play&utm_medium=organic) a non-event: "GOOGLE-PLAY" is not a
            // creator code, so the lookup returns null.
            var creator = await TryEnterAsync(candidate, SourceInstallReferrer);

            // Cleared on a definitive answer only. A null creator means the server SAID this
            // is not a creator code — retrying cannot change that. A throw means we never
            // reached the server, and the value stays for the next launch.
            // Empty rather than a delete: GetValueAsync reads blank back as null, so this is
            // "unset" by its documented contract and needs no new store method.
            await _settings.SetValueAsync(KeyPendingReferrer, string.Empty);

            if (creator != null)
                Debug.WriteLine($"[Referral] install referrer credited to {creator}");
        }
        catch (Exception ex)
        {
            // Offline, or the server is unhappy. The candidate is still stored, so the next
            // launch tries again. Play retains the referrer for 90 days; this costs nothing
            // and recovers the whole population that first-launches without a connection.
            Debug.WriteLine($"[Referral] install referrer failed: {ex.Message}");
        }
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

            // The source the code originally arrived by, so a link-driven install stays a
            // link-driven install even though the claim happens later, on sign-in.
            var source = await _settings.GetValueAsync(KeySource) ?? SourceTyped;
            var creator = await CallAsync("enter_creator_code", code, session.AccessToken, source);
            if (creator == null)
            {
                // The code was retired or renamed between typing and signing in. Mark it
                // claimed anyway: retrying forever on every launch cannot make it valid.
                Debug.WriteLine($"[Referral] pending code '{code}' is no longer a creator code");
                await _settings.SetFlagAsync(KeyClaimed, true);
                return;
            }

            await RememberAsync(code, creator, claimed: true, source);
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
    /// whether the call is authenticated. <c>lookup_creator_code</c> takes no source (it
    /// records nothing), so the argument is omitted rather than sent and ignored.</summary>
    private async Task<string?> CallAsync(string function, string code, string? accessToken, string? source = null)
    {
        object args = source == null
            ? new { p_code = code }
            : new { p_code = code, p_source = source };

        var doc = await _http.RpcAsync(function, args, accessToken ?? string.Empty);
        if (doc == null || doc.RootElement.ValueKind != JsonValueKind.String)
            return null;
        var creator = doc.RootElement.GetString();
        return string.IsNullOrWhiteSpace(creator) ? null : creator;
    }

    private async Task RememberAsync(string code, string creator, bool claimed, string source)
    {
        await _settings.SetValueAsync(KeyCode, code);
        await _settings.SetValueAsync(KeyCreator, creator);
        await _settings.SetValueAsync(KeySource, source);
        await _settings.SetFlagAsync(KeyClaimed, claimed);

        // Every subsequent event carries the channel, exactly like account_state. Set from
        // the creator NAME, never the code: this is a label for how the app was found.
        AnalyticsContext.ReferralSource = creator;

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
    public Task InitializeAsync() => Task.CompletedTask;
}
