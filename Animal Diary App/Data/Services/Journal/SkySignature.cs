namespace Animal_Diary_App.Data.Services.Journal;

// ─────────────────────────────────────────────────────────────────────────────
//  Everything about one pet's sky that is DECORATION.
//
//  One pet, one signature: the figure of stars, the shape of the timeline's wave,
//  the arrangement of the dust and bubbles, and the one colour the atmosphere leans
//  towards all come out of a single seed derived from the pet's name. Charly's sky
//  and Luna's sky are recognisably different skies, not the same sky with different
//  dots on it.
//
//  ── The line, again, because this is where it would be easiest to cross ──
//  NOTHING here may derive from anything recorded, and nothing here may change what
//  a reader can work out from the picture. Symbol, position and size are the meaning
//  and they are untouched: two pets with identical diaries would still have every
//  event at the same moment, wearing the same symbol, at the same size. What differs
//  is the room it is drawn in.
//
//  Every number is bounded. The bands are deliberately narrow — the point is that no
//  pet can draw a BAD sky, only a different one — and they are pinned by tests,
//  because "we widened one constant" is exactly how a decorative range turns into a
//  sky that nobody would want to share.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// The decorative fingerprint of one pet's sky. Pure numbers and colour token names;
/// resolved to actual colours by the drawable through <c>AppColors</c>, the same way
/// an owner-defined tracker's colour is.
/// </summary>
public readonly record struct SkySignature
{
    private SkySignature(
        ulong seed,
        double phaseLong,
        double phaseShort,
        double longWavelength,
        double shortWavelength,
        double longWeight,
        double amplitude,
        string accentKey)
    {
        Seed = seed;
        PhaseLong = phaseLong;
        PhaseShort = phaseShort;
        LongWavelength = longWavelength;
        ShortWavelength = shortWavelength;
        LongWeight = longWeight;
        Amplitude = amplitude;
        AccentKey = accentKey;
    }

    /// <summary>The raw seed, for the layers that generate themselves from a position
    /// (dust, bubbles) and for the pet's figure.</summary>
    public ulong Seed { get; }

    /// <summary>Where the long sweep starts. The cheapest variation there is and the
    /// most visible: it decides whether the timeline enters the card rising or
    /// falling.</summary>
    public double PhaseLong { get; }
    public double PhaseShort { get; }

    /// <summary>Divisors of x, so the true wavelength is 2π× these — about two turns
    /// across a phone's width at the middle of the band.</summary>
    public double LongWavelength { get; }
    public double ShortWavelength { get; }

    /// <summary>How much of the wave is the long sweep rather than the ripple. Low in
    /// the band gives a busier, more restless line; high gives a calm arc.</summary>
    public double LongWeight { get; }
    public double ShortWeight => 1 - LongWeight;

    /// <summary>How much of the canvas's height the wave uses.</summary>
    public double Amplitude { get; }

    /// <summary>Colour token the atmosphere leans towards — see the note on
    /// <see cref="Accents"/>.</summary>
    public string AccentKey { get; }

    // ── The bands ────────────────────────────────────────────────────────────────
    // Centred on the values the sky was tuned to by hand; the widths are what a sky
    // can vary by while still being one of the good ones.
    private const double MinLongWavelength = 43;
    private const double MaxLongWavelength = 64;
    private const double MinShortWavelength = 13;
    private const double MaxShortWavelength = 22;
    private const double MinLongWeight = 0.72;
    private const double MaxLongWeight = 0.88;
    private const double MinAmplitude = 0.24;
    private const double MaxAmplitude = 0.32;

    /// <summary>
    /// The five hues an atmosphere may lean towards — the app's own five accents, one
    /// step further muted for a night ground.
    ///
    /// <para><b>Deliberately their own tokens rather than the <c>Star*</c> set.</b> The
    /// Star colours say WHICH KIND of thing was recorded; if the whole sky could glow in
    /// one of them, a pet whose ambient hue happened to be rose would have a rose wash
    /// behind their rose glucose readings, and the legend would stop being a promise.
    /// These are dimmer and greyer than any category, so they can only ever read as
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

    /// <summary>The sky for a pet with no name yet — the hand-tuned middle of every
    /// band. Never <c>default(SkySignature)</c>, whose zeroed wavelengths would divide
    /// a position by nothing.</summary>
    public static SkySignature Default { get; } = new(
        seed: 1,
        phaseLong: 0,
        phaseShort: 1.7,
        longWavelength: 52,
        shortWavelength: 17,
        longWeight: 0.8,
        amplitude: 0.28,
        accentKey: "SkyAccentTeal");

    /// <summary>
    /// One pet's signature. Same name, same sky, on every device and after every app
    /// update — the hash and the generator are both hand-rolled for that reason (see
    /// <see cref="ConstellationAsterism"/>).
    /// </summary>
    public static SkySignature For(string? name, int birthYear = 0)
    {
        var key = (name ?? string.Empty).Trim().ToLowerInvariant();
        if (key.Length == 0)
            return Default;

        var seed = Hash(key, birthYear);
        var rng = new SeedWalk(seed);

        return new SkySignature(
            seed: seed,
            phaseLong: rng.NextDouble() * Math.Tau,
            phaseShort: rng.NextDouble() * Math.Tau,
            longWavelength: Between(ref rng, MinLongWavelength, MaxLongWavelength),
            shortWavelength: Between(ref rng, MinShortWavelength, MaxShortWavelength),
            longWeight: Between(ref rng, MinLongWeight, MaxLongWeight),
            amplitude: Between(ref rng, MinAmplitude, MaxAmplitude),
            accentKey: Accents[rng.Next(Accents.Count)]);
    }

    private static double Between(ref SeedWalk rng, double low, double high) =>
        low + rng.NextDouble() * (high - low);

    /// <summary>FNV-1a over the name, with the birth year folded in. Deliberately not
    /// <see cref="string.GetHashCode()"/>: that is randomised per process in .NET, so
    /// every launch would be a different sky.</summary>
    internal static ulong Hash(string key, int birthYear)
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
            return hash == 0 ? 1 : hash;
        }
    }

    /// <summary>A tiny xorshift64*, so every derived number is reproducible everywhere.
    /// <see cref="Random"/> gives no guarantee that a seed produces the same sequence
    /// across runtime versions, and a pet's sky has to survive an app update.</summary>
    internal struct SeedWalk
    {
        private ulong _state;

        public SeedWalk(ulong seed) => _state = seed == 0 ? 1 : seed;

        public ulong NextRaw()
        {
            unchecked
            {
                _state ^= _state >> 12;
                _state ^= _state << 25;
                _state ^= _state >> 27;
                return _state * 2685821657736338717UL;
            }
        }

        public double NextDouble() => (NextRaw() >> 11) / (double)(1UL << 53);

        public int Next(int exclusiveMax) =>
            exclusiveMax <= 0 ? 0 : (int)(NextRaw() % (ulong)exclusiveMax);
    }
}
