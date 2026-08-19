namespace Animal_Diary_App.Data.Services;

/// <summary>
/// A monotonic counter that changes whenever anything on this device's data might
/// have changed. It exists so a page can answer one question cheaply: <i>is what I
/// am already showing still current?</i>
///
/// <para><b>Why.</b> Both tab pages run their full load on every appearance, with no
/// check of any kind — so Today → Journal → Today re-read the database twice over,
/// two or three dozen round trips each, for a screen that had not changed. On a slow
/// device that is most of what "switching menus feels heavy" is.</para>
///
/// <para><b>What bumps it.</b> <see cref="SyncStamp.Touch"/> — which every repository
/// write already funnels through, the same hook the cloud engine uses to notice local
/// changes — and the sync engine when a pull actually applies remote rows.</para>
///
/// <para><b>Why a version alone is not the guard.</b> Some paths write rows without a
/// stamp: demo seeding inserts raw (its rows deliberately carry no SyncId), and the
/// hard-delete purges bypass the tombstone path. Rather than enumerate those — a list
/// that is silently wrong the day someone adds the next one — the guard that consumes
/// this pairs it with a short freshness window, so a bump nobody remembered to make
/// costs a few seconds of staleness instead of a page that never refreshes again.
/// Treat the list above as "what makes the guard fast", never as "what makes it
/// correct".</para>
/// </summary>
public static class DataVersion
{
    private static int _version;

    /// <summary>The current value. Any change means "reload rather than trust what you
    /// have"; the number itself carries no meaning.</summary>
    public static int Current => Volatile.Read(ref _version);

    /// <summary>Mark the device's data as changed.</summary>
    public static void Bump() => Interlocked.Increment(ref _version);

    /// <summary>Subscribe to the write hook. Called once from <c>App</c>'s constructor;
    /// idempotent, so a second call cannot double-count.</summary>
    public static void Initialize()
    {
        if (Interlocked.Exchange(ref _initialized, 1) == 1)
            return;
        SyncStamp.RowTouched += Bump;
    }

    private static int _initialized;
}

/// <summary>
/// What a page's last completed load was a load OF. Two loads with the same key would
/// paint the same pixels, so the second can be skipped.
/// </summary>
/// <param name="Version">The <see cref="DataVersion.Current"/> at load time.</param>
/// <param name="PetId">The active pet — switching pets writes no row, so the version
/// alone would not notice it.</param>
/// <param name="Day">The date the page was showing, which also covers the app being
/// left open across midnight.</param>
public readonly record struct PageLoadKey(int Version, int PetId, DateTime Day)
{
    /// <summary>How long a completed load may be trusted. Short on purpose: it is the
    /// backstop for any write path that doesn't bump the version (see
    /// <see cref="DataVersion"/>), and it bounds how stale a clock-derived detail — a
    /// dose becoming due while you were on another tab — can get. Long enough that
    /// glancing at another tab and coming straight back is free, which is the whole
    /// point.</summary>
    public static readonly TimeSpan Freshness = TimeSpan.FromSeconds(30);
}
