namespace Animal_Diary_App.Data.Services.Cloud;

using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// One signed-in Supabase session: the tokens plus the little identity the UI
/// shows. Persisted as JSON in <see cref="SecureStorage"/> (never the plain
/// preferences — these are credentials), loaded once per app run by
/// <see cref="CloudAuthService"/>.
/// </summary>
public sealed class CloudSession
{
    [JsonPropertyName("access")] public string AccessToken { get; set; } = string.Empty;
    [JsonPropertyName("refresh")] public string RefreshToken { get; set; } = string.Empty;
    [JsonPropertyName("userId")] public string UserId { get; set; } = string.Empty;
    [JsonPropertyName("email")] public string Email { get; set; } = string.Empty;
    [JsonPropertyName("expiresAt")] public DateTime ExpiresAtUtc { get; set; }

    private const string StorageKey = "felova_cloud_session";

    public static async Task<CloudSession?> LoadAsync()
    {
        try
        {
            var json = await SecureStorage.Default.GetAsync(StorageKey);
            return string.IsNullOrEmpty(json) ? null : JsonSerializer.Deserialize<CloudSession>(json);
        }
        catch
        {
            // A corrupt/unreadable secure store degrades to signed-out, never a crash.
            return null;
        }
    }

    public async Task SaveAsync()
    {
        await SecureStorage.Default.SetAsync(StorageKey, JsonSerializer.Serialize(this));
    }

    public static void Clear() => SecureStorage.Default.Remove(StorageKey);
}
