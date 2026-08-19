namespace Animal_Diary_App.Data.Services.Journal;

// ─────────────────────────────────────────────────────────────────────────────
//  The one decorative thing about a pet's sky: the colour its atmosphere leans
//  towards, derived from the pet's name.
//
//  It used to carry far more — a wave shape, a starfield seed, a figure of joined
//  stars. All of that existed to give a MEANINGLESS y axis something to be, and it
//  went when the axis got a meaning. What survives is the one piece that never
//  competed with the data: a faint tint, so two pets' screenshots are not
//  interchangeable.
//
//  It encodes nothing. It is derived from who the pet IS and never from anything
//  recorded, so it is identical on their first day and their fifth year.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>The decorative fingerprint of one pet's sky: a colour token name,
/// resolved through <c>Helpers/AppColors</c> the same way an owner-defined tracker's
/// colour is.</summary>
public readonly record struct SkySignature(string AccentKey)
{
    /// <summary>
    /// The five hues an atmosphere may lean towards — the app's own five accents, one
    /// step further muted for a night ground.
    ///
    /// <para><b>Deliberately their own tokens rather than the <c>Star*</c> set.</b> The
    /// Star colours say WHICH KIND of thing was recorded; if a whole sky could glow in
    /// one of them, a pet whose ambient hue happened to be rose would have a rose wash
    /// behind their rose glucose readings, and the legend would stop being a promise.
    /// These are dimmer and greyer than any category, so they can only read as
    /// weather.</para>
    /// </summary>
    public static IReadOnlyList<string> Accents { get; } = new[]
    {
        "SkyAccentTeal",
        "SkyAccentBlue",
        "SkyAccentHoney",
        "SkyAccentRose",
        "SkyAccentViolet",
    };

    /// <summary>The sky for a pet with no name yet.</summary>
    public static SkySignature Default { get; } = new(Accents[0]);

    /// <summary>
    /// One pet's signature. Same name, same colour, on every device and after every
    /// app update — which is why the hash is hand-rolled: <see cref="string.GetHashCode()"/>
    /// is randomised per process in .NET, so every launch would be a different sky.
    /// </summary>
    public static SkySignature For(string? name, int birthYear = 0)
    {
        var key = (name ?? string.Empty).Trim().ToLowerInvariant();
        if (key.Length == 0)
            return Default;

        return new SkySignature(Accents[(int)(Hash(key, birthYear) % (ulong)Accents.Count)]);
    }

    /// <summary>FNV-1a over the name, with the birth year folded in so two dogs called
    /// Bella are two skies.</summary>
    private static ulong Hash(string key, int birthYear)
    {
        unchecked
        {
            var hash = 14695981039346656037UL;
            foreach (var c in key)
            {
                hash ^= c;
                hash *= 1099511628211UL;
            }

            hash ^= (uint)birthYear;
            hash *= 1099511628211UL;
            return hash;
        }
    }
}
