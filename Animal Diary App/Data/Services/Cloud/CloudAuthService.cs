namespace Animal_Diary_App.Data.Services.Cloud;

using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Animal_Diary_App.Data.Services.Analytics;
using Microsoft.Maui.Authentication;

/// <summary>
/// The only auth API the app uses (mirrors <c>IAnalyticsService</c> /
/// <c>INotificationService</c> — nothing above this knows GoTrue exists).
/// Sign-up and password recovery verify with the 6-digit emailed code, not a
/// link — there is no website to land on (see supabase/README.md).
/// </summary>
public interface ICloudAuthService
{
    /// <summary>Last-known state for synchronous UI binding; authoritative state
    /// comes from <see cref="GetSessionAsync"/>.</summary>
    bool IsSignedIn { get; }
    string? Email { get; }
    string? UserId { get; }
    /// <summary>In-memory session expiry (no refresh/network) — for the dev panel.</summary>
    DateTime? SessionExpiresUtc { get; }

    /// <summary>Raised on sign-in, sign-out, and expiry — Settings re-renders on it.</summary>
    event Action? SessionChanged;

    /// <summary>The current session with a fresh access token (refreshing if it is
    /// about to expire), or null when signed out. An unrecoverable refresh failure
    /// clears the session and raises <see cref="SessionChanged"/>.</summary>
    Task<CloudSession?> GetSessionAsync(bool forceRefresh = false);

    Task SignUpAsync(string email, string password);
    Task ResendSignUpCodeAsync(string email);
    Task VerifySignUpAsync(string email, string code);
    Task SignInAsync(string email, string password);

    /// <summary>Sign in via Google in the system browser (Supabase OAuth + PKCE).
    /// Throws <see cref="OperationCanceledException"/> if the user backs out.
    /// Android-only in the UI (a social login on iOS would require Sign in with
    /// Apple — deferred until iOS ships).</summary>
    Task SignInWithGoogleAsync();

    Task SignOutAsync();

    Task RequestPasswordResetAsync(string email);
    /// <summary>Verify the recovery code and set the new password. Ends signed in.</summary>
    Task VerifyPasswordResetAsync(string email, string code, string newPassword);
}

public sealed class CloudAuthService : ICloudAuthService
{
    private readonly CloudHttp _http;
    private readonly IAnalyticsService _analytics;
    // Serializes session load/refresh so parallel callers can't double-refresh
    // (GoTrue rotates the refresh token — a stale second refresh would sign us out).
    private readonly SemaphoreSlim _gate = new(1, 1);
    private CloudSession? _session;
    private bool _loaded;

    public CloudAuthService(CloudHttp http, IAnalyticsService analytics)
    {
        _http = http;
        _analytics = analytics;
    }

    // The single place session state changes are mirrored to the analytics context so
    // every event can carry a coarse account_state. Only the boolean is shared — no id,
    // email, or token ever crosses into analytics.
    private void SyncAnalyticsState() => AnalyticsContext.IsSignedIn = _session != null;

    public bool IsSignedIn => _session != null;
    public string? Email => _session?.Email;
    public string? UserId => _session?.UserId;
    /// <summary>In-memory session expiry for the dev panel — a pure read, never
    /// triggers a refresh or network call.</summary>
    public DateTime? SessionExpiresUtc => _session?.ExpiresAtUtc;
    public event Action? SessionChanged;

    public async Task<CloudSession?> GetSessionAsync(bool forceRefresh = false)
    {
        await _gate.WaitAsync();
        try
        {
            if (!_loaded)
            {
                _session = await CloudSession.LoadAsync();
                _loaded = true;
                SyncAnalyticsState();
            }
            if (_session == null)
                return null;

            // Refresh a minute early so an in-flight sync never straddles expiry.
            if (forceRefresh || _session.ExpiresAtUtc <= DateTime.UtcNow.AddMinutes(1))
            {
                try
                {
                    var doc = await _http.AuthPostAsync("token?grant_type=refresh_token",
                        new { refresh_token = _session.RefreshToken });
                    _session = ParseSession(doc!);
                    await _session.SaveAsync();
                    CloudDiagnostics.Record($"[Cloud] session refreshed (forced={forceRefresh}); next expiry {_session.ExpiresAtUtc:HH:mm:ss}Z");
                }
                catch (CloudException ex) when (ex.Kind == CloudErrorKind.Network)
                {
                    // Offline: hand back the stale session — data calls will fail
                    // with Network too and the sync cycle reports "offline" cleanly.
                    CloudDiagnostics.Record("[Cloud] session refresh deferred (offline); keeping stale session");
                    return _session;
                }
                catch (CloudException ex)
                {
                    // The refresh token itself was rejected — we are signed out.
                    CloudDiagnostics.Record($"[Cloud] SIGNED OUT — refresh rejected ({ex.Kind}, {ex.StatusCode})");
                    ClearLocked();
                    return null;
                }
            }
            return _session;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SignUpAsync(string email, string password)
    {
        // With "Confirm email" ON this returns a user but NO session — the flow
        // continues in VerifySignUpAsync with the emailed code.
        await _http.AuthPostAsync("signup", new { email, password });
        // The account now exists (awaiting verification). Fires only on success, so the
        // gap to sign_up_verified is the real email-code drop-off. No email is sent.
        _analytics.Track(AnalyticsEvents.SignUpStarted);
    }

    public async Task ResendSignUpCodeAsync(string email)
    {
        await _http.AuthPostAsync("resend", new { type = "signup", email });
    }

    public async Task VerifySignUpAsync(string email, string code)
    {
        var doc = await _http.AuthPostAsync("verify", new { type = "signup", email, token = code });
        await StoreSessionAsync(ParseSession(doc!));
        // Account creation completed — the funnel's bottom. No email/id attached.
        _analytics.Track(AnalyticsEvents.SignUpVerified);
    }

    public async Task SignInAsync(string email, string password)
    {
        var doc = await _http.AuthPostAsync("token?grant_type=password", new { email, password });
        await StoreSessionAsync(ParseSession(doc!));
        _analytics.Track(AnalyticsEvents.SignIn);
    }

    // The deep-link the browser flow returns to. Must be in Supabase's
    // "Redirect URLs" allow-list, and the Android callback activity declares the
    // matching scheme intent-filter (see Platforms/Android + supabase/README.md).
    private const string OAuthCallback = "felova://auth-callback";

    public async Task SignInWithGoogleAsync()
    {
        // PKCE: send a challenge so GoTrue returns a one-time ?code= in the query
        // (robust — unlike the implicit flow's #fragment, a query survives the
        // https→custom-scheme redirect) and never exposes tokens in the browser.
        var verifier = CreateCodeVerifier();
        var authorizeUrl =
            $"{CloudConfig.Url}/auth/v1/authorize?provider=google" +
            $"&redirect_to={Uri.EscapeDataString(OAuthCallback)}" +
            $"&code_challenge={CreateCodeChallenge(verifier)}&code_challenge_method=s256";

        CloudDiagnostics.Record("[Cloud] Google sign-in: opening browser");

        // WebAuthenticator drives the system browser and resumes on the deep-link;
        // it throws TaskCanceledException if the user dismisses it.
        WebAuthenticatorResult result;
        try
        {
            result = await WebAuthenticator.Default.AuthenticateAsync(
                new Uri(authorizeUrl), new Uri(OAuthCallback));
        }
        catch (TaskCanceledException)
        {
            // NOTE: WebAuthenticator raises this both on genuine user-dismiss AND
            // when the redirect fails to resume (e.g. app process recycled while
            // the browser was foreground) — the two are indistinguishable here.
            CloudDiagnostics.Record("[Cloud] Google sign-in: browser returned no result (cancelled or dropped)");
            throw;
        }

        if (result.Properties.TryGetValue("error", out var error))
        {
            CloudDiagnostics.Record($"[Cloud] Google sign-in error: {(result.Properties.TryGetValue("error_description", out var d) ? d : error)}");
            throw new CloudException(CloudErrorKind.Other, 0,
                result.Properties.TryGetValue("error_description", out var d2) ? d2 : error);
        }

        if (!result.Properties.TryGetValue("code", out var code) || string.IsNullOrEmpty(code))
        {
            CloudDiagnostics.Record("[Cloud] Google sign-in: callback carried no authorization code");
            throw new CloudException(CloudErrorKind.Other, 0, "Google sign-in returned no authorization code.");
        }

        CloudDiagnostics.Record("[Cloud] Google sign-in: code received, exchanging");

        // Exchange the code for a real session (same token shape as password grant).
        var doc = await _http.AuthPostAsync("token?grant_type=pkce",
            new { auth_code = code, code_verifier = verifier });
        await StoreSessionAsync(ParseSession(doc!));
        // Browser/PKCE flows can silently drop (see the TaskCanceledException note above),
        // so measuring successful completions matters. The event carries no identity.
        _analytics.Track(AnalyticsEvents.GoogleSignIn);
        CloudDiagnostics.Record($"[Cloud] Google sign-in: SUCCESS ({_session?.Email}); expiry {_session?.ExpiresAtUtc:HH:mm:ss}Z");
    }

    private static string CreateCodeVerifier() => Base64Url(RandomNumberGenerator.GetBytes(64));

    private static string CreateCodeChallenge(string verifier)
        => Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

    private static string Base64Url(byte[] data)
        => Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public async Task SignOutAsync()
    {
        var session = _session;
        if (session != null)
        {
            try { await _http.AuthPostAsync("logout", new { }, session.AccessToken); }
            catch (CloudException) { /* best effort — local sign-out always succeeds */ }
        }

        await _gate.WaitAsync();
        try { ClearLocked(); }
        finally { _gate.Release(); }
    }

    public async Task RequestPasswordResetAsync(string email)
    {
        await _http.AuthPostAsync("recover", new { email });
    }

    public async Task VerifyPasswordResetAsync(string email, string code, string newPassword)
    {
        var doc = await _http.AuthPostAsync("verify", new { type = "recovery", email, token = code });
        var session = ParseSession(doc!);
        await _http.AuthPutUserAsync(new { password = newPassword }, session.AccessToken);
        await StoreSessionAsync(session);
    }

    private async Task StoreSessionAsync(CloudSession session)
    {
        await _gate.WaitAsync();
        try
        {
            _session = session;
            _loaded = true;
            SyncAnalyticsState();
            await session.SaveAsync();
        }
        finally
        {
            _gate.Release();
        }
        SessionChanged?.Invoke();
    }

    private void ClearLocked()
    {
        _session = null;
        SyncAnalyticsState();
        CloudSession.Clear();
        SessionChanged?.Invoke();
    }

    private static CloudSession ParseSession(JsonDocument doc)
    {
        var root = doc.RootElement;
        var expiresAt = root.TryGetProperty("expires_at", out var ea) && ea.ValueKind == JsonValueKind.Number
            ? DateTimeOffset.FromUnixTimeSeconds(ea.GetInt64()).UtcDateTime
            : DateTime.UtcNow.AddSeconds(root.GetProperty("expires_in").GetInt32());

        var user = root.GetProperty("user");
        return new CloudSession
        {
            AccessToken = root.GetProperty("access_token").GetString() ?? string.Empty,
            RefreshToken = root.GetProperty("refresh_token").GetString() ?? string.Empty,
            ExpiresAtUtc = expiresAt,
            UserId = user.GetProperty("id").GetString() ?? string.Empty,
            Email = user.TryGetProperty("email", out var em) ? em.GetString() ?? string.Empty : string.Empty
        };
    }
}
