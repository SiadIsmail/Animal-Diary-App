namespace Animal_Diary_App.Data.Services.Cloud;

using System.Globalization;
using System.Text.Json;

/// <summary>
/// Value conversions between the local SQLite shapes and the cloud JSON shapes.
/// Conventions (mirrored in supabase/migrations):
/// - date-only values travel as "yyyy-MM-dd" (no timezone reinterpretation);
/// - times of day travel as .NET TimeSpan ticks (bigint);
/// - timestamps travel as ISO-8601. Local DateTimes with unspecified kind are
///   serialized AS IF UTC — the round-trip is stable and identical on every
///   device, which matters more here than absolute-instant correctness.
/// </summary>
internal static class CloudJson
{
    public static string ToIso(DateTime value)
        => DateTime.SpecifyKind(value, DateTimeKind.Utc).ToString("o", CultureInfo.InvariantCulture);

    public static DateTime ParseIso(string value)
        => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture).UtcDateTime;

    public static string ToDateOnly(DateTime value)
        => value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static DateTime ParseDateOnly(string value)
        => DateTime.ParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture);

    // ── JsonElement readers (tolerate null/absent as the natural default) ──

    public static string GetString(JsonElement row, string name)
        => row.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String
            ? p.GetString() ?? string.Empty : string.Empty;

    public static string? GetStringOrNull(JsonElement row, string name)
        => row.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

    public static int GetInt(JsonElement row, string name)
        => row.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Number ? p.GetInt32() : 0;

    public static int? GetIntOrNull(JsonElement row, string name)
        => row.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Number ? p.GetInt32() : null;

    public static long? GetLongOrNull(JsonElement row, string name)
        => row.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Number ? p.GetInt64() : null;

    public static decimal GetDecimal(JsonElement row, string name)
        => row.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Number ? p.GetDecimal() : 0m;

    public static decimal? GetDecimalOrNull(JsonElement row, string name)
        => row.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Number ? p.GetDecimal() : null;

    public static bool GetBool(JsonElement row, string name)
        => row.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.True;

    public static DateTime GetIsoDateTime(JsonElement row, string name)
        => ParseIso(GetString(row, name));

    public static DateTime? GetIsoDateTimeOrNull(JsonElement row, string name)
    {
        var s = GetStringOrNull(row, name);
        return s == null ? null : ParseIso(s);
    }

    public static TimeSpan GetTicksTime(JsonElement row, string name)
        => new(GetLongOrNull(row, name) ?? 0);

    /// <summary>True when the row carries a non-null deleted_at — the cloud's
    /// soft-delete marker, mapped to the local IsDeleted flag.</summary>
    public static bool IsDeleted(JsonElement row)
        => row.TryGetProperty("deleted_at", out var p) && p.ValueKind == JsonValueKind.String;
}
