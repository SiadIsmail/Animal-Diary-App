namespace Animal_Diary_App.Data.Services.Cloud;

using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

/// <summary>Redeeming an access code, for the UI. The billing half of this class is
/// <see cref="Billing.IGrantSource"/>, which is what the gate reads.</summary>
public interface ICloudAccessCodeService
{
    /// <summary>Redeem a code and return when the resulting grant ends. Throws
    /// <see cref="CloudException"/> with an <c>AccessCode*</c> kind the sheet localizes;
    /// raw server text only ever reaches <see cref="Debug.WriteLine"/>.</summary>
    Task<DateTime> RedeemAsync(string code);
}

/// <summary>
/// The account's access-code grant: redeem one, cache the resulting expiry, and answer the
/// billing gate from that cache.
///
/// <para><b>Deliberately not part of the sync cycle.</b> <c>CloudSyncService.SyncNowAsync</c>
/// returns early with <c>BackupDisabled</c> when backup is off, and a grant has to reach
/// someone who signed in <i>only</i> to redeem a code. So this fetches on its own, driven by
/// <c>EntitlementService.RefreshAsync</c> (launch + resume) rather than by a sync run.</para>
///
/// <para><b>The cache is account-scoped on purpose.</b> It lives under the <c>cloud:</c>
/// prefix, which <c>CloudSyncService.SignOutTeardownAsync</c> clears wholesale — so signing
/// out gives up the grant on this device. The alternative (AppSettings, which survives
/// sign-out) would mean one code could cover an unlimited number of accounts: redeem, sign
/// out, sign in as someone else, keep both.</para>
/// </summary>
public sealed class CloudAccessCodeService : ICloudAccessCodeService, Billing.IGrantSource
{
    // INVARIANT: cloud:-prefixed, like every other account-scoped key. See the key list in
    // CloudSyncService — anything that must SURVIVE a sign-out belongs in AppSettings instead.
    private const string KeyGrantedUntil = "cloud:grantedUntil";
    private const string KeyEverGranted = "cloud:everGranted";

    private readonly CloudHttp _http;
    private readonly ICloudAuthService _auth;
    private readonly SyncStateStore _state;

    // Written from a background refresh, read synchronously by the gate on the UI thread.
    // Both are single-word reads, and neither is part of a wider invariant with the other,
    // so volatile is enough; there is nothing here to tear.
    private volatile bool _loaded;
    private DateTime? _grantedUntil;
    private volatile bool _everGranted;

    public CloudAccessCodeService(CloudHttp http, ICloudAuthService auth, SyncStateStore state)
    {
        _http = http;
        _auth = auth;
        _state = state;

        // Signing out clears the cached grant from memory the moment the session goes, not
        // whenever the next refresh happens to run. The SyncState rows are removed by the
        // sign-out teardown; this keeps the in-memory copy from outliving them.
        _auth.SessionChanged += () =>
        {
            if (_auth.IsSignedIn)
                return;
            _grantedUntil = null;
            _everGranted = false;
            _loaded = false;
        };
    }

    // ── IGrantSource: what the billing gate reads ───────────────────────────

    /// <summary>Unknown ONLY while a signed-in device still owes its first fetch. Signed out
    /// or cloud-disabled has nothing to wait for and reports true — reporting false there
    /// would hold the gate open forever for local-only users.</summary>
    public bool GrantKnown => _loaded || !CloudConfig.Enabled || !_auth.IsSignedIn;

    /// <summary>A cached absolute instant, so this stays true offline until the grant
    /// genuinely ends. That is why a grant needs no offline grace window the way
    /// sponsorship does.</summary>
    public bool IsGranted => _grantedUntil is DateTime until && DateTime.UtcNow < until;

    public DateTime? GrantedUntilUtc => _grantedUntil;

    public bool EverGranted => _everGranted;

    public async Task RefreshAsync()
    {
        if (!CloudConfig.Enabled)
            return;

        // Load the cache before anything can read the gate. Cheap, and it means an offline
        // launch still knows about the grant.
        if (!_loaded)
            await LoadCacheAsync();

        if (!_auth.IsSignedIn)
            return;

        try
        {
            var session = await _auth.GetSessionAsync();
            if (session == null)
                return;

            var doc = await _http.RpcAsync("my_access", new { }, session.AccessToken);
            DateTime? until = null;
            if (doc != null && doc.RootElement.ValueKind == JsonValueKind.String)
                until = CloudJson.ParseIso(doc.RootElement.GetString()!);

            await StoreAsync(until);
        }
        catch (Exception ex)
        {
            // Offline, or the server is unhappy. The cached value stands: a granted user at
            // the vet with no signal must keep working.
            Debug.WriteLine($"[Cloud] grant refresh failed: {ex.Message}");
        }
    }

    // ── redeeming ───────────────────────────────────────────────────────────

    public async Task<DateTime> RedeemAsync(string code)
    {
        var session = await _auth.GetSessionAsync()
            ?? throw new CloudException(CloudErrorKind.AuthExpired, 401,
                "redeem: not signed in");

        var doc = await _http.RpcRequiredAsync(
            "redeem_access_code",
            new { p_code = (code ?? string.Empty).Trim().ToUpperInvariant() },
            session.AccessToken);

        // The RPC returns a bare timestamptz. Anything else means the function changed under
        // us, which must name itself rather than surfacing as a parse crash.
        if (doc.RootElement.ValueKind != JsonValueKind.String)
        {
            throw new CloudException(CloudErrorKind.Other, 0,
                "rpc/redeem_access_code: no grant end in the response");
        }

        var until = CloudJson.ParseIso(doc.RootElement.GetString()!);

        // Effective immediately and without a follow-up sync: the RPC hands back the new
        // expiry, so someone who redeems on a flaky connection is not left locked out
        // waiting for a refresh that may not run for minutes.
        await StoreAsync(until);
        return until;
    }

    // ── cache ───────────────────────────────────────────────────────────────

    private async Task LoadCacheAsync()
    {
        try
        {
            var raw = await _state.GetAsync(KeyGrantedUntil);
            _grantedUntil = string.IsNullOrEmpty(raw) ? null : CloudJson.ParseIso(raw);
            _everGranted = await _state.GetAsync(KeyEverGranted) == "1";
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Cloud] grant cache load failed: {ex.Message}");
        }
        finally
        {
            // Set even on failure: a device that cannot read its own cache must not sit in
            // the optimistic "unknown" state forever, which would hold the gate open.
            _loaded = true;
        }
    }

    private async Task StoreAsync(DateTime? until)
    {
        _grantedUntil = until;

        // Written even when the server says "no grant", so a signed-in account with none
        // resolves to KNOWN on the next launch instead of leaving the gate optimistically
        // open. Same reason CloudSyncService writes its access cache when it is empty.
        if (until is DateTime u)
        {
            await _state.SetAsync(KeyGrantedUntil, u.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
            if (!_everGranted)
            {
                _everGranted = true;
                await _state.SetAsync(KeyEverGranted, "1");
            }
        }
        else
        {
            await _state.RemoveAsync(KeyGrantedUntil);
        }

        _loaded = true;
    }
}

/// <summary>Registered when the cloud is switched off. Nothing to redeem and nothing
/// granted, so the gate falls back entirely to the trial and the store.</summary>
public sealed class NullCloudAccessCodeService : ICloudAccessCodeService, Billing.IGrantSource
{
    public bool GrantKnown => true;
    public bool IsGranted => false;
    public DateTime? GrantedUntilUtc => null;
    public bool EverGranted => false;
    public Task RefreshAsync() => Task.CompletedTask;

    public Task<DateTime> RedeemAsync(string code)
        => throw new CloudException(CloudErrorKind.Other, 0, "redeem: cloud is disabled");
}
