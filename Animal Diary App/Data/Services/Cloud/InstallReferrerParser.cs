namespace Animal_Diary_App.Data.Services.Cloud;

/// <summary>
/// Pulls a creator candidate out of Google Play's install referrer string.
///
/// <para>Its own file, free of MAUI and SQLite, so the parsing decision can be unit-tested,
/// the same reason <see cref="SignOutImpact"/> is separated out of the service that uses it.
/// The input is attacker-adjacent (anyone can craft a Play link with any referrer they
/// like), so its behaviour on junk is worth pinning down in tests rather than discovering
/// in the field.</para>
/// </summary>
public static class InstallReferrerParser
{
    /// <summary>
    /// The candidate creator code carried by a referrer, or null when there is none.
    ///
    /// <para>The referrer is a URL-encoded query fragment, not a bare value:
    /// <c>creator=theto</c>, <c>utm_source=theto&amp;utm_medium=video</c>. <c>creator</c>
    /// wins over <c>utm_source</c> so one link can carry ordinary UTM tags for other tools
    /// without the two confusing each other.</para>
    ///
    /// <para><b>This decides nothing about whether the value is real.</b> Every organic Play
    /// install carries <c>utm_source=google-play&amp;utm_medium=organic</c>, so this returns
    /// "google-play" for the majority of installs, which is correct and harmless, because
    /// the caller validates the candidate against the actual creator codes server-side and
    /// gets null back. Never treat a returned value as attribution on its own.</para>
    /// </summary>
    public static string? Parse(string? referrer)
    {
        if (string.IsNullOrWhiteSpace(referrer))
            return null;

        string? utmSource = null;
        foreach (var pair in referrer.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var split = pair.Split('=', 2);
            if (split.Length != 2)
                continue;

            string key, value;
            try
            {
                key = Uri.UnescapeDataString(split[0]).Trim();
                value = Uri.UnescapeDataString(split[1]).Trim();
            }
            catch (UriFormatException)
            {
                // A malformed escape sequence ("%zz") in a string we did not write. Skip the
                // pair rather than losing the whole referrer to one bad segment.
                continue;
            }

            if (value.Length == 0)
                continue;

            // First match wins for `creator`, so a crafted link cannot append a second one
            // and override the first.
            if (key.Equals("creator", StringComparison.OrdinalIgnoreCase))
                return value;
            if (utmSource == null && key.Equals("utm_source", StringComparison.OrdinalIgnoreCase))
                utmSource = value;
        }

        return utmSource;
    }
}
