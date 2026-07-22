namespace Animal_Diary_App.Data.Services.Cloud;

using System.Net;
using System.Text;
using System.Text.Json;

/// <summary>Coarse buckets the UI can localize; the raw server message only ever
/// goes to debug logs, never to the owner.</summary>
public enum CloudErrorKind
{
    Network,            // offline / DNS / timeout — quiet, expected, retry later
    InvalidCredentials, // wrong email/password
    EmailTaken,         // sign-up with an existing address
    EmailNotConfirmed,  // signed in before entering the code
    InvalidCode,        // wrong/expired OTP
    WeakPassword,       // below Supabase's password policy
    RateLimited,        // 429 — mostly the built-in email sender's cap
    AuthExpired,        // refresh token no longer valid → signed out
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
/// The one type that speaks HTTP to Supabase (GoTrue auth + PostgREST data) —
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

    private static async Task<JsonDocument?> SendAsync(HttpMethod method, string url, object? body, string? accessToken)
    {
        HttpResponseMessage response;
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

            response = await Http.SendAsync(request);
            text = await response.Content.ReadAsStringAsync();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            // Offline is a normal state for this app, not an error condition.
            throw new CloudException(CloudErrorKind.Network, 0, ex.Message);
        }

        if (!response.IsSuccessStatusCode)
            throw Classify((int)response.StatusCode, text);

        return string.IsNullOrWhiteSpace(text) ? null : JsonDocument.Parse(text);
    }

    /// <summary>Map a Supabase error body onto the coarse kinds the UI knows. The
    /// matching is heuristic on purpose — unknown errors fall through to Other and
    /// show a generic message rather than leaking server text to the owner.</summary>
    private static CloudException Classify(int status, string body)
    {
        var lower = body.ToLowerInvariant();

        CloudErrorKind kind;
        if (status == (int)HttpStatusCode.TooManyRequests || lower.Contains("rate limit"))
            kind = CloudErrorKind.RateLimited;
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
