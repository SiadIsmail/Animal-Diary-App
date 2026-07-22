namespace Animal_Diary_App.Data.Services.Cloud;

using System.Diagnostics;
using System.Text.Json;

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
    Task SignOutAsync();

    Task RequestPasswordResetAsync(string email);
    /// <summary>Verify the recovery code and set the new password. Ends signed in.</summary>
    Task VerifyPasswordResetAsync(string email, string code, string newPassword);
}

public sealed class CloudAuthService : ICloudAuthService
{
    private readonly CloudHttp _http;
    // Serializes session load/refresh so parallel callers can't double-refresh
    // (GoTrue rotates the refresh token — a stale second refresh would sign us out).
    private readonly SemaphoreSlim _gate = new(1, 1);
    private CloudSession? _session;
    private bool _loaded;

    public CloudAuthService(CloudHttp http)
    {
        _http = http;
    }

    public bool IsSignedIn => _session != null;
    public string? Email => _session?.Email;
    public string? UserId => _session?.UserId;
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
                }
                catch (CloudException ex) when (ex.Kind == CloudErrorKind.Network)
                {
                    // Offline: hand back the stale session — data calls will fail
                    // with Network too and the sync cycle reports "offline" cleanly.
                    return _session;
                }
                catch (CloudException ex)
                {
                    // The refresh token itself was rejected — we are signed out.
                    Debug.WriteLine($"[Cloud] session refresh failed: {ex.Kind}");
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
    }

    public async Task ResendSignUpCodeAsync(string email)
    {
        await _http.AuthPostAsync("resend", new { type = "signup", email });
    }

    public async Task VerifySignUpAsync(string email, string code)
    {
        var doc = await _http.AuthPostAsync("verify", new { type = "signup", email, token = code });
        await StoreSessionAsync(ParseSession(doc!));
    }

    public async Task SignInAsync(string email, string password)
    {
        var doc = await _http.AuthPostAsync("token?grant_type=password", new { email, password });
        await StoreSessionAsync(ParseSession(doc!));
    }

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
