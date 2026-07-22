namespace Animal_Diary_App.Data.Services.Cloud;

using System.Diagnostics;
using System.Text.Json;
using Animal_Diary_App.Data.Services.Analytics;
using Animal_Diary_App.Data.Services.Notifications;

public enum SyncOutcome
{
    Success,
    Offline,        // network unavailable — normal, retried on the next trigger
    NotSignedIn,
    BackupDisabled, // signed in but the owner hasn't enabled backup
    AuthExpired,    // session died; UI shows signed-out
    AlreadyRunning, // coalesced into the in-flight run
    Failed
}

/// <summary>
/// The one cloud-sync API the app sees. Everything is fire-and-forget-safe:
/// offline and races degrade to "try again on the next trigger", never a crash.
/// When <c>CloudConfig.Enabled</c> is false, <see cref="NullCloudSyncService"/>
/// is registered instead and none of this exists at runtime.
/// </summary>
public interface ICloudSyncService
{
    bool IsBackupEnabled { get; }
    DateTime? LastSyncedUtc { get; }

    /// <summary>Raised after every completed run and on enable/disable — the
    /// Settings surface re-renders on it.</summary>
    event Action? StateChanged;

    /// <summary>Load persisted flags (called once at startup, off the UI path).</summary>
    Task InitializeAsync();

    /// <summary>One pull→apply→push cycle. Serialized; concurrent calls coalesce.</summary>
    Task<SyncOutcome> SyncNowAsync();

    /// <summary>Debounced trigger for "something changed / app resumed".</summary>
    void RequestSyncSoon();

    /// <summary>Opt this device's data into the account: marks everything dirty
    /// (and re-mints sync identities if the device previously synced to a
    /// DIFFERENT account) and runs the first sync.</summary>
    Task<SyncOutcome> EnableBackupAsync();

    /// <summary>Stop syncing (local-only again). Cloud data stays; sign-out separate.</summary>
    Task DisableBackupAsync();

    /// <summary>The reset arm: soft-delete owned pets cloud-side + leave shared
    /// pets (rpc delete_my_data). Caller wipes local data afterwards.</summary>
    Task DeleteCloudDataAsync();

    /// <summary>Hard account deletion (rpc delete_my_account) + local sign-out.</summary>
    Task DeleteAccountAsync();
}

public sealed class CloudSyncService : ICloudSyncService
{
    // SyncState keys — the engine owns this vocabulary (see SyncStateStore).
    private const string KeyEnabled = "cloud:backupEnabled";
    private const string KeyAccount = "cloud:lastAccount";
    private const string KeyLastSynced = "cloud:lastSynced";
    private const string KeyFirstBackupDone = "cloud:firstBackupDone";
    private const string KeyCursorPrefix = "cloud:cursor:";

    // Push batches must stay smaller than pull pages: all rows of one RPC commit
    // share one server timestamp, and the cursor can only advance safely when a
    // tie-set never spans a page boundary.
    private const int PushBatchSize = 200;
    private const int PullPageSize = 1000;
    private static readonly TimeSpan Debounce = TimeSpan.FromSeconds(45);

    private readonly AppDatabase _db;
    private readonly CloudHttp _http;
    private readonly ICloudAuthService _auth;
    private readonly SyncStateStore _state;
    private readonly MedicationReminderScheduler _reminders;
    private readonly IAnalyticsService _analytics;
    private readonly IReadOnlyList<ITableSync> _tables = SyncTableMaps.Build();

    // One run at a time; a request during a run coalesces into one follow-up run.
    private readonly SemaphoreSlim _runGate = new(1, 1);
    private bool _runQueued;
    private CancellationTokenSource? _debounce;

    private bool _enabled;
    private DateTime? _lastSynced;

    public CloudSyncService(
        AppDatabase db,
        CloudHttp http,
        ICloudAuthService auth,
        SyncStateStore state,
        MedicationReminderScheduler reminders,
        IAnalyticsService analytics)
    {
        _db = db;
        _http = http;
        _auth = auth;
        _state = state;
        _reminders = reminders;
        _analytics = analytics;

        // Every repository write funnels through SyncStamp — that one hook is the
        // whole "detect local changes" mechanism (see coding-standards.md).
        SyncStamp.RowTouched += RequestSyncSoon;
    }

    public bool IsBackupEnabled => _enabled;
    public DateTime? LastSyncedUtc => _lastSynced;
    public event Action? StateChanged;

    public async Task InitializeAsync()
    {
        _enabled = await _state.GetAsync(KeyEnabled) == "1";
        var last = await _state.GetAsync(KeyLastSynced);
        _lastSynced = last == null ? null : CloudJson.ParseIso(last);
    }

    public void RequestSyncSoon()
    {
        if (!_enabled || !_auth.IsSignedIn)
            return;
        _debounce?.Cancel();
        var cts = _debounce = new CancellationTokenSource();
        Task.Run(async () =>
        {
            try
            {
                await Task.Delay(Debounce, cts.Token);
                await SyncNowAsync();
            }
            catch (TaskCanceledException) { /* superseded by a newer write */ }
            catch (Exception ex) { Debug.WriteLine($"[Cloud] debounced sync failed: {ex.Message}"); }
        });
    }

    public async Task<SyncOutcome> SyncNowAsync()
    {
        if (!_enabled)
            return _auth.IsSignedIn ? SyncOutcome.BackupDisabled : SyncOutcome.NotSignedIn;

        if (!_runGate.Wait(0))
        {
            _runQueued = true;
            return SyncOutcome.AlreadyRunning;
        }

        try
        {
            SyncOutcome outcome;
            do
            {
                _runQueued = false;
                outcome = await RunOnceAsync();
            }
            while (_runQueued && outcome == SyncOutcome.Success);
            return outcome;
        }
        finally
        {
            _runGate.Release();
        }
    }

    private async Task<SyncOutcome> RunOnceAsync()
    {
        var session = await _auth.GetSessionAsync();
        if (session == null)
            return SyncOutcome.NotSignedIn;

        try
        {
            var ctx = new SyncRunContext(_db.Connection);

            // Pull before push: conflicts resolve against the freshest server
            // state, and a brand-new device naturally does a full download.
            foreach (var table in _tables)
                await PullTableAsync(table, ctx, session);

            // Remote schedule/dose changes must re-materialize this device's own
            // reminder instances — reuse the idempotent scheduler path.
            foreach (var medId in ctx.AffectedMedications)
            {
                try { await _reminders.SyncMedicationAsync(medId); }
                catch (Exception ex) { Debug.WriteLine($"[Cloud] reminder re-sync {medId} failed: {ex.Message}"); }
            }

            foreach (var table in _tables)
                await PushTableAsync(table, ctx, session);

            _lastSynced = DateTime.UtcNow;
            await _state.SetAsync(KeyLastSynced, CloudJson.ToIso(_lastSynced.Value));

            if (await _state.GetAsync(KeyFirstBackupDone) != "1")
            {
                await _state.SetAsync(KeyFirstBackupDone, "1");
                _analytics.Track(AnalyticsEvents.CloudBackupCompleted);
            }

            return SyncOutcome.Success;
        }
        catch (CloudException ex) when (ex.Kind == CloudErrorKind.Network)
        {
            return SyncOutcome.Offline;
        }
        catch (CloudException ex) when (ex.Kind == CloudErrorKind.AuthExpired)
        {
            // One forced refresh; if the session is truly dead GetSessionAsync
            // clears it and the UI hears SessionChanged.
            if (await _auth.GetSessionAsync(forceRefresh: true) != null)
                return await RunOnceAsync();
            return SyncOutcome.AuthExpired;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Cloud] sync failed: {ex}");
            return SyncOutcome.Failed;
        }
        finally
        {
            StateChanged?.Invoke();
        }
    }

    private async Task PullTableAsync(ITableSync table, SyncRunContext ctx, CloudSession session)
    {
        var cursorKey = KeyCursorPrefix + table.CloudTable;
        var cursor = await _state.GetAsync(cursorKey) ?? "1970-01-01T00:00:00Z";

        while (true)
        {
            var path = $"{table.CloudTable}?select=*&order=updated_at.asc,id.asc" +
                       $"&updated_at=gt.{Uri.EscapeDataString(cursor)}&limit={PullPageSize}";
            var doc = await _http.RestGetAsync(path, session.AccessToken);
            var rows = doc!.RootElement;
            var count = rows.GetArrayLength();
            if (count == 0)
                return;

            await table.ApplyRowsAsync(ctx, rows);

            cursor = CloudJson.GetString(rows[count - 1], "updated_at");
            await _state.SetAsync(cursorKey, cursor);

            if (count < PullPageSize)
                return;
        }
    }

    private async Task PushTableAsync(ITableSync table, SyncRunContext ctx, CloudSession session)
    {
        var pending = await table.CollectDirtyAsync(ctx);
        for (int i = 0; i < pending.Count; i += PushBatchSize)
        {
            var batch = pending.Skip(i).Take(PushBatchSize).ToList();
            await _http.RpcAsync("push_rows", new
            {
                p_table = table.CloudTable,
                p_rows = batch.Select(p => p.Payload).ToList()
            }, session.AccessToken);

            // The server accepted the batch; clear the flags (each guard skips
            // rows that were written again while the push was in flight).
            foreach (var p in batch)
                await p.ClearDirtyAsync();
        }
    }

    public async Task<SyncOutcome> EnableBackupAsync()
    {
        var session = await _auth.GetSessionAsync();
        if (session == null)
            return SyncOutcome.NotSignedIn;

        // A device that previously synced to a DIFFERENT account must not push
        // rows whose ids exist under that other account — re-mint every identity
        // and start over as fresh uploads.
        var lastAccount = await _state.GetAsync(KeyAccount);
        if (lastAccount != null && lastAccount != session.UserId)
            await ResetSyncIdentityAsync();

        await MarkAllDirtyAsync();
        await _state.SetAsync(KeyAccount, session.UserId);
        await _state.SetAsync(KeyEnabled, "1");
        _enabled = true;
        _analytics.Track(AnalyticsEvents.CloudEnabled);
        StateChanged?.Invoke();

        return await SyncNowAsync();
    }

    public async Task DisableBackupAsync()
    {
        _enabled = false;
        await _state.SetAsync(KeyEnabled, "0");
        StateChanged?.Invoke();
    }

    public async Task DeleteCloudDataAsync()
    {
        var session = await _auth.GetSessionAsync();
        if (session == null)
            return;
        await _http.RpcAsync("delete_my_data", new { }, session.AccessToken);
    }

    public async Task DeleteAccountAsync()
    {
        var session = await _auth.GetSessionAsync();
        if (session == null)
            return;
        await _http.RpcAsync("delete_my_account", new { }, session.AccessToken);
        // The auth user is gone server-side; drop the local session without the
        // (now-failing) logout round-trip.
        await _auth.SignOutAsync();
        await DisableBackupAsync();
    }

    /// <summary>Queue every active row for upload (tombstones stay local noise).</summary>
    private async Task MarkAllDirtyAsync()
    {
        foreach (var t in LocalTableNames)
            await _db.Connection.ExecuteAsync($"update \"{t}\" set IsDirty = 1 where IsDeleted = 0");
    }

    /// <summary>Fresh GUIDs for every active row + cleared cursors, so a switch to
    /// a new account uploads clean copies instead of colliding with rows owned by
    /// the previous account.</summary>
    private async Task ResetSyncIdentityAsync()
    {
        await _db.Connection.RunInTransactionAsync(conn =>
        {
            foreach (var t in LocalTableNames)
            {
                conn.Execute($"update \"{t}\" set IsDirty = 0 where IsDeleted = 1");
                var ids = conn.QueryScalars<int>($"select Id from \"{t}\" where IsDeleted = 0");
                foreach (var id in ids)
                    conn.Execute($"update \"{t}\" set SyncId = ? where Id = ?", Guid.NewGuid().ToString(), id);
            }
        });

        foreach (var table in _tables)
            await _state.RemoveAsync(KeyCursorPrefix + table.CloudTable);
        await _state.RemoveAsync(KeyFirstBackupDone);
    }

    private static readonly string[] LocalTableNames =
    {
        "Pet", "PetEntry", "Medication", "MedicationSchedule", "MedicationDoseLog",
        "Tracker", "PetCondition", "GlucoseEntry", "AppetiteEntry", "SeizureEntry"
    };
}
