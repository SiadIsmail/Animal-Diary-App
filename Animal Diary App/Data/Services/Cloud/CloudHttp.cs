namespace Animal_Diary_App.Data.Services.Cloud;

using System.Net;
using System.Text;
using System.Text.Json;

/// <summary>Coarse buckets the UI can localize; the raw server message only ever
/// goes to debug logs, never to the owner.</summary>
public enum CloudErrorKind
{
    Network,            // offline / DNS / timeout: quiet, expected, retry later
    InvalidCredentials, // wrong email/password
    EmailTaken,         // sign-up with an existing address
    EmailNotConfirmed,  // signed in before entering the code
    InvalidCode,        // wrong/expired OTP
    WeakPassword,       // below Supabase's password policy
    RateLimited,        // 429 / server-side attempt caps
    AuthExpired,        // refresh token no longer valid → signed out
    InviteInvalid,      // unknown, expired, or used-up invite code
    InviteAlreadyMember,// redeeming a code for a pet you already have
    CarerLimitReached,  // the pet's (or the owner's) caregiver cap is full
    AccessCodeInvalid,  // unknown, expired, or used-up ACCESS code (not an invite)
    AccessCodeUsed,     // an access code already redeemed by this account
    Other
}

/// <summary>A failed cloud call, pre-bucketed for the UI.</summary>
public sealed class CloudException : Exception
{
    public CloudErrorKind Kind { get; }
    public int StatusCode { get; }

    public CloudException(CloudErrorKind kind, int statusCode, string message)
        : base(message)
    {
        Kind = kind;
        StatusCode = statusCode;
    }
}

/// <summary>
/// The one type that speaks HTTP to Supabase (GoTrue auth + PostgREST data),
/// hand-built over <see cref="HttpClient"/>, mirroring the PostHog decision:
/// the tiny API surface we use (a handful of auth endpoints, range selects, one
/// RPC) isn't worth a client SDK dependency, and building the requests by hand
/// makes what leaves the device explicit. Everything above this speaks
/// <see cref="CloudException"/>, never raw HTTP.
/// </summary>
public sealed class CloudHttp
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    /// <summary>POST an auth (GoTrue) endpoint, e.g. "signup" or "token?grant_type=password".
    /// Returns the parsed body (or null for empty responses).</summary>
    public async Task<JsonDocument?> AuthPostAsync(string pathAndQuery, object body, string? accessToken = null)
        => await SendAsync(HttpMethod.Post, $"{CloudConfig.Url}/auth/v1/{pathAndQuery}", body, accessToken);

    /// <summary>PUT to the auth user endpoint (password update after recovery).</summary>
    public async Task<JsonDocument?> AuthPutUserAsync(object body, string accessToken)
        => await SendAsync(HttpMethod.Put, $"{CloudConfig.Url}/auth/v1/user", body, accessToken);

    /// <summary>GET a PostgREST path (already query-string-encoded by the caller).</summary>
    public async Task<JsonDocument?> RestGetAsync(string pathAndQuery, string accessToken)
        => await SendAsync(HttpMethod.Get, $"{CloudConfig.Url}/rest/v1/{pathAndQuery}", null, accessToken);

    /// <summary>POST a PostgREST RPC by name.</summary>
    public async Task<JsonDocument?> RpcAsync(string function, object args, string accessToken)
        => await SendAsync(HttpMethod.Post, $"{CloudConfig.Url}/rest/v1/rpc/{function}", args, accessToken);

    // ── The "a body is required" variants ────────────────────────────────────────
    // A 2xx with an empty body is a real outcome of SendAsync, so every caller that
    // then reads .RootElement had to write `doc!`: turning that case into a
    // NullReferenceException instead of the CloudException this whole layer exists to
    // produce. It surfaced as "Object reference not set" in the diagnostics log, which
    // is exactly the unnamed failure the "cloud failures must name themselves" decision
    // forbids. These wrappers name it instead.

    public async Task<JsonDocument> AuthPostRequiredAsync(string pathAndQuery, object body, string? accessToken = null)
        => Require(await AuthPostAsync(pathAndQuery, body, accessToken), $"auth/{Trim(pathAndQuery)}");

    public async Task<JsonDocument> RestGetRequiredAsync(string pathAndQuery, string accessToken)
        => Require(await RestGetAsync(pathAndQuery, accessToken), $"rest/{Trim(pathAndQuery)}");

    public async Task<JsonDocument> RpcRequiredAsync(string function, object args, string accessToken)
        => Require(await RpcAsync(function, args, accessToken), $"rpc/{function}");

    private static JsonDocument Require(JsonDocument? doc, string what) =>
        doc ?? throw new CloudException(
            CloudErrorKind.Other, 0, $"{what}: server returned success with an empty body");

    /// <summary>Endpoint name without its query string: enough to locate the call,
    /// and it cannot carry a value from the row being synced.</summary>
    private static string Trim(string pathAndQuery)
    {
        var q = pathAndQuery.IndexOf('?');
        return q < 0 ? pathAndQuery : pathAndQuery[..q];
    }

    private static async Task<JsonDocument?> SendAsync(HttpMethod method, string url, object? body, string? accessToken)
    {
        int status;
        string text;
        try
        {
            using var request = new HttpRequestMessage(method, url);
            request.Headers.Add("apikey", CloudConfig.PublishableKey);
            // The apikey header alone is anonymous; the bearer token is what RLS sees.
            if (!string.IsNullOrEmpty(accessToken))
                request.Headers.Add("Authorization", $"Bearer {accessToken}");
            if (body != null)
                request.Content = new StringContent(
                    body as string ?? JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

            // Disposed here, unlike the request-only `using` this used to have: the
            // response owns the content stream, and an undisposed one holds the
            // connection until the GC gets to it.
            using var response = await Http.SendAsync(request);
            status = (int)response.StatusCode;
            text = await response.Content.ReadAsStringAsync();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            // Offline is a normal state for this app, not an error condition.
            CloudDiagnostics.Record($"[Cloud] {method} {Endpoint(url)} → network error: {ex.Message}");
            throw new CloudException(CloudErrorKind.Network, 0, ex.Message);
        }

        if (status is < 200 or > 299)
        {
            // Single choke point for every server error: trimmed body, no tokens.
            var trimmed = text.Length > 300 ? text[..300] : text;
            CloudDiagnostics.Record($"[Cloud] {method} {Endpoint(url)} → {status}: {trimmed}");
            throw Classify(status, text);
        }

        return string.IsNullOrWhiteSpace(text) ? null : JsonDocument.Parse(text);
    }

    /// <summary>The path portion of a Supabase URL, for readable logs (the host is
    /// constant; access tokens live in a header, never the URL).</summary>
    private static string Endpoint(string url)
    {
        var i = url.IndexOf("/auth/", StringComparison.Ordinal);
        if (i < 0) i = url.IndexOf("/rest/", StringComparison.Ordinal);
        return i < 0 ? url : url[i..];
    }

    /// <summary>Map a Supabase error body onto the coarse kinds the UI knows. The
    /// matching is heuristic on purpose: unknown errors fall through to Other and
    /// show a generic message rather than leaking server text to the owner.</summary>
    private static CloudException Classify(int status, string body)
    {
        var lower = body.ToLowerInvariant();

        CloudErrorKind kind;
        // Before the rate-limit check: a full carer list is a real, actionable limit, not a
        // "slow down". Matches the shared wording of all three cap errors in migration 0010.
        if (lower.Contains("maximum number of carers"))
            kind = CloudErrorKind.CarerLimitReached;
        // Access codes are checked BEFORE the invite phrases and before the rate limit, and
        // migration 0015 deliberately does not reuse redeem_invite's "invalid or expired
        // code" wording. Both codes are typed into a box that looks the same, so a bad
        // access code falling through to InviteInvalid would tell someone their pet INVITE
        // was invalid. Keep these two arms above the invite ones.
        else if (lower.Contains("unknown or used access code"))
            kind = CloudErrorKind.AccessCodeInvalid;
        else if (lower.Contains("access code is already on your account"))
            kind = CloudErrorKind.AccessCodeUsed;
        else if (status == (int)HttpStatusCode.TooManyRequests || lower.Contains("rate limit") || lower.Contains("too many"))
            kind = CloudErrorKind.RateLimited;
        else if (lower.Contains("invalid or expired code"))
            kind = CloudErrorKind.InviteInvalid;
        else if (lower.Contains("already a member"))
            kind = CloudErrorKind.InviteAlreadyMember;
        else if (lower.Contains("invalid login credentials") || lower.Contains("invalid_credentials"))
            kind = CloudErrorKind.InvalidCredentials;
        else if (lower.Contains("already registered") || lower.Contains("user_already_exists") || lower.Contains("email_exists"))
            kind = CloudErrorKind.EmailTaken;
        else if (lower.Contains("not confirmed") || lower.Contains("email_not_confirmed"))
            kind = CloudErrorKind.EmailNotConfirmed;
        else if (lower.Contains("otp_expired") || lower.Contains("token has expired") || lower.Contains("invalid otp") || lower.Contains("otp_disabled"))
            kind = CloudErrorKind.InvalidCode;
        else if (lower.Contains("weak_password") || lower.Contains("password should"))
            kind = CloudErrorKind.WeakPassword;
        else if (lower.Contains("refresh_token") || (status == 401 && lower.Contains("invalid")))
            kind = CloudErrorKind.AuthExpired;
        else if (status == 401)
            kind = CloudErrorKind.AuthExpired;
        else
            kind = CloudErrorKind.Other;

        return new CloudException(kind, status, body);
    }
}
