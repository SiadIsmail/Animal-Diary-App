namespace Animal_Diary_App.Data.Models;

/// <summary>
/// <b>Which tracker</b>, when "which" is no longer answerable by an enum alone.
///
/// <para>A tracker is either one of the six the app ships with (<see cref="TrackerId"/>)
/// or one the owner made up: a walk, a groom, a poop. Both kinds have to be a
/// dictionary key in the pending engine, so the two identities need one type. That is
/// all this is: a tagged union of "built-in X" and "the owner's custom tracker #N".</para>
///
/// <para><b>Why not just add <c>TrackerId.Custom</c>:</b> every custom tracker would
/// then share one key, and <see cref="Services.Journal.PendingEngine"/> looks its entry
/// dates up by exactly that key. Five custom trackers would collapse into one, and
/// logging a walk would tick the groom off the list.</para>
///
/// <para>The implicit conversion from <see cref="TrackerId"/> is load-bearing: it lets
/// every existing call site that names a built-in keep reading the way it always did
/// (<c>TrackerVisuals.For(TrackerId.Mood)</c>), so this type is invisible until you are
/// actually holding a custom one.</para>
/// </summary>
/// <param name="BuiltIn">The shipped tracker this key names, or null when it names a
/// custom one.</param>
/// <param name="CustomId">The <c>CustomTracker</c> row id when <paramref name="BuiltIn"/>
/// is null; 0 otherwise.</param>
public readonly record struct TrackerKey(TrackerId? BuiltIn, int CustomId)
{
    /// <summary>Whether this names an owner-created tracker rather than a shipped one.</summary>
    public bool IsCustom => BuiltIn is null;

    /// <summary>The key for one of the six shipped trackers.</summary>
    public static implicit operator TrackerKey(TrackerId id) => new(id, 0);

    /// <summary>The key for an owner-created tracker, by its <c>CustomTracker</c> row id.</summary>
    public static TrackerKey Custom(int customTrackerId) => new(null, customTrackerId);

    /// <summary>True when this key names <paramref name="id"/>. Reads better than
    /// comparing against a converted key, and is null-safe on the custom side.</summary>
    public bool Is(TrackerId id) => BuiltIn == id;

    // default(TrackerKey) is (null, 0): a custom tracker with no row, which cannot
    // exist. That is deliberate: an accidentally-defaulted key matches nothing rather
    // than quietly meaning "glucose", which is what a non-nullable BuiltIn would give.
    public override string ToString() => IsCustom ? $"custom:{CustomId}" : BuiltIn!.ToString()!;
}
