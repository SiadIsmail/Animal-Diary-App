namespace Animal_Diary_App.Data.Services.Cloud;

using System.Diagnostics;
using System.Text.Json;
using Animal_Diary_App.Data.Models;
using Animal_Diary_App.Data.Services.Analytics;
using Animal_Diary_App.Data.Services.Notifications;
using Animal_Diary_App.Helpers;

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

    /// <summary>Raised after a sync that actually CHANGED local data (applied
    /// remote rows or purged a revoked pet). The visible page re-runs its normal
    /// OnAppearing load on this, so another caregiver's entries show up without
    /// tab-switching. Raised from a background thread — subscribers marshal.</summary>
    event Action? RemoteChangesApplied;

    /// <summary>Load persisted flags (called once at startup, off the UI path).</summary>
    Task InitializeAsync();

    /// <summary>One pull→apply→push cycle. Serialized; concurrent calls coalesce.</summary>
    Task<SyncOutcome> SyncNowAsync();

    /// <summary>Debounced trigger for "something changed / app resumed".</summary>
    void RequestSyncSoon();

    /// <summary>App lifecycle hook (resume/sleep). Foreground gates the periodic
    /// pull — a backgrounded app must not keep polling the network.</summary>
    void NotifyAppState(bool foreground);

    /// <summary>Opt this device's data into the account: marks everything dirty
    /// (and re-mints sync identities if the device previously synced to a
    /// DIFFERENT account) and runs the first sync.</summary>
    Task<SyncOutcome> EnableBackupAsync();

    /// <summary>Stop syncing (local-only again). Cloud data stays; sign-out separate.
    /// <b>Local data stays too</b> — this is the reversible exit ("these are still my pets,
    /// this device just stops syncing"). Do not collapse it into sign-out, which is the
    /// exit that removes them.</summary>
    Task DisableBackupAsync();

    /// <summary>
    /// Step 1 of signing out: push anything outstanding (best effort, needs a connection),
    /// then report what signing out will actually cost. The caller shows this and asks; it
    /// changes nothing on its own.
    /// </summary>
    Task<SignOutImpact> PrepareSignOutAsync();

    /// <summary>
    /// Step 2 of signing out: remove every pet belonging to the account being left and clear
    /// all account-scoped sync state. Call <b>before</b> <c>ICloudAuthService.SignOutAsync</c>
    /// — this needs the membership map that signing out invalidates.
    ///
    /// <para>Only ever for a <b>deliberate</b> sign-out. An expired refresh token also clears
    /// the session, but that is the same account involuntarily: the cursors are still valid,
    /// the user will sign back in, and tearing down there would delete their pets because
    /// their token aged out.</para>
    /// </summary>
    /// <returns>How many pets remain on the device afterwards. Zero means the app has
    /// nothing left to show and belongs back in onboarding — the same routing the owner's
    /// last-pet deletion already does.</returns>
    Task<int> SignOutTeardownAsync();

    /// <summary>The caller's role for a pet ("owner" / "caregiver"), or null when
    /// unknown / not a member / never synced. Keyed by the pet's SyncId; refreshed
    /// on every sync from the cloud membership list.</summary>
    string? GetPetRole(string petSyncId);

    /// <summary>True when this user owns at least one pet that someone else is caring for.
    /// Used for exactly one thing: when the owner's own access ends, telling them their
    /// carers just went read-only too — they are the only person who can change that.</summary>
    bool OwnsASharedPet { get; }

    /// <summary>Raised when a pet's SPONSORSHIP changed between two syncs — the owner
    /// subscribed, lapsed, or their trial ran out. Carries the pets that just lost
    /// sponsored access, so the UI can say so once rather than letting the app quietly
    /// go read-only. Raised from a background thread; subscribers marshal.
    ///
    /// <para>Distinct from <see cref="RemoteChangesApplied"/>: no local row changed, only
    /// what this user is allowed to write. Also distinct from losing MEMBERSHIP, which
    /// purges the pet outright and needs no message.</para></summary>
    event Action<IReadOnlyList<string>>? SponsorshipRevoked;

    /// <summary>The reset arm: soft-delete owned pets cloud-side + leave shared
    /// pets (rpc delete_my_data). Caller wipes local data afterwards.</summary>
    Task DeleteCloudDataAsync();

    /// <summary>Hard account deletion (rpc delete_my_account) + local sign-out.</summary>
    Task DeleteAccountAsync();
}

/// <summary>One cached row of <c>list_my_pet_access</c>, as persisted in SyncState.
/// Public only so <c>System.Text.Json</c> can round-trip it.</summary>
public sealed record PetAccessRow(string Role, bool OwnerAccess, int CarerCount, DateTime FetchedUtc);


public sealed class CloudSyncService : ICloudSyncService, Billing.IPetAccessSource
{
    // SyncState keys — the engine owns this vocabulary (see SyncStateStore).
    //
    // INVARIANT: every key below is account-scoped and MUST carry this prefix, so
    // SignOutTeardownAsync's single ClearPrefixAsync can never miss one. Anything that must
    // survive a sign-out (the trial anchor, language, preferences) belongs in AppSettings,
    // which is device-scoped — not here.
    private const string CloudStatePrefix = "cloud:";

    private const string KeyEnabled = "cloud:backupEnabled";
    private const string KeyAccount = "cloud:lastAccount";
    private const string KeyLastSynced = "cloud:lastSynced";
    private const string KeyFirstBackupDone = "cloud:firstBackupDone";
    private const string KeyCursorPrefix = "cloud:cursor:";
    private const string KeyMemberships = "cloud:memberships";

    // A NEW key, deliberately not a reshape of KeyMemberships: the old value is a
    // Dictionary<string,string> and a device upgrading into this build would fail to
    // deserialize it, emptying the role cache until the next successful sync (the sharing
    // sheet would read "not synced yet" for every pet in the meantime). A new key lets the
    // old one keep answering GetPetRole until this one is populated.
    private const string KeyPetAccess = "cloud:petAccess:v2";

    // Push batches must stay smaller than pull pages: all rows of one RPC commit
    // share one server timestamp, and the cursor can only advance safely when a
    // tie-set never spans a page boundary.
    private const int PushBatchSize = 200;
    private const int PullPageSize = 1000;
    private static readonly TimeSpan Debounce = TimeSpan.FromSeconds(45);

    // Foreground polling cadence: another caregiver's entries should surface
    // while the owner sits on a page, without real-time infrastructure. A no-op
    // cycle is ~a dozen tiny range GETs — cheap at this interval.
    private static readonly TimeSpan ForegroundPoll = TimeSpan.FromMinutes(3);

    private readonly AppDatabase _db;
    private readonly CloudHttp _http;
    private readonly ICloudAuthService _auth;
    private readonly SyncStateStore _state;
    private readonly MedicationReminderScheduler _reminders;
    private readonly ActivePetService _activePet;
    private readonly IAnalyticsService _analytics;
    private readonly Billing.ITrialAnchor _trialAnchor;
    // Read for one thing only: whether signing out costs a redeemed access code. The grant
    // itself is fetched and cached by CloudAccessCodeService, deliberately outside this
    // cycle (a grant must reach someone who never turned backup on).
    private readonly Billing.IGrantSource _grants;
    private readonly IReadOnlyList<ITableSync> _tables = SyncTableMaps.Build();

    // One run at a time; a request during a run coalesces into one follow-up run.
    // _runQueued is written by callers that lost the gate and read by the holder, so it
    // crosses threads without the gate protecting it — volatile, or a coalesced request
    // can be dropped and its write sits unsynced until the next trigger.
    private readonly SemaphoreSlim _runGate = new(1, 1);
    private volatile bool _runQueued;

    // The open debounce window, or null when none is pending. Guarded by _debounceGate
    // because RowTouched fires from whatever thread performed the write.
    private readonly object _debounceGate = new();
    private CancellationTokenSource? _debounce;

    private bool _enabled;
    private volatile bool _foreground = true;
    private DateTime? _lastSynced;

    // Swapped whole from the sync thread, read from the UI thread (GetPetRole feeds
    // PetDeletionService.DetermineKind — owner-deletes-for-everyone vs caregiver-leaves).
    // Same treatment as _petAccess below, for the same reason.
    private volatile Dictionary<string, string> _petRoles = new();

    // Sponsorship cache (pet SyncId → role + whether that pet's owner had access when we
    // last asked). Read synchronously by the billing gate on the UI thread, rewritten by
    // reference from the sync thread — so it is swapped whole, never mutated in place.
    private volatile Dictionary<string, PetAccessRow> _petAccess = new();
    private volatile bool _accessLoaded;

    public CloudSyncService(
        AppDatabase db,
        CloudHttp http,
        ICloudAuthService auth,
        SyncStateStore state,
        MedicationReminderScheduler reminders,
        ActivePetService activePet,
        IAnalyticsService analytics,
        Billing.ITrialAnchor trialAnchor,
        Billing.IGrantSource grants)
    {
        _db = db;
        _http = http;
        _auth = auth;
        _state = state;
        _reminders = reminders;
        _activePet = activePet;
        _analytics = analytics;
        _trialAnchor = trialAnchor;
        _grants = grants;

        // Every repository write funnels through SyncStamp — that one hook is the
        // whole "detect local changes" mechanism (see coding-standards.md).
        SyncStamp.RowTouched += RequestSyncSoon;

        // The foreground poll loop lives for the process; the flags inside make
        // it a pure no-op whenever cloud is off, signed out, or backgrounded.
        Task.Run(async () =>
        {
            while (true)
            {
                await Task.Delay(ForegroundPoll);
                if (!_foreground || !_enabled || !_auth.IsSignedIn)
                    continue;
                try { await SyncNowAsync(); }
                catch (Exception ex) { Debug.WriteLine($"[Cloud] periodic sync failed: {ex.Message}"); }
            }
        });
    }

    public void NotifyAppState(bool foreground)
    {
        _foreground = foreground;
        if (foreground)
            RequestSyncSoon();
    }

    public bool IsBackupEnabled => _enabled;
    public DateTime? LastSyncedUtc => _lastSynced;
    public event Action? StateChanged;
    public event Action? RemoteChangesApplied;
    public event Action<IReadOnlyList<string>>? SponsorshipRevoked;

    public async Task InitializeAsync()
    {
        _enabled = await _state.GetAsync(KeyEnabled) == "1";
        var last = await _state.GetAsync(KeyLastSynced);
        _lastSynced = last == null ? null : CloudJson.ParseIso(last);

        var roles = await _state.GetAsync(KeyMemberships);
        if (roles != null)
        {
            try { _petRoles = JsonSerializer.Deserialize<Dictionary<string, string>>(roles) ?? new(); }
            catch { _petRoles = new(); }
        }

        var access = await _state.GetAsync(KeyPetAccess);
        if (access != null)
        {
            try
            {
                _petAccess = JsonSerializer.Deserialize<Dictionary<string, PetAccessRow>>(access) ?? new();
                _accessLoaded = true;
            }
            catch { _petAccess = new(); }
        }
    }

    public string? GetPetRole(string petSyncId)
        => _petRoles.TryGetValue(petSyncId, out var role) ? role : null;

    public bool OwnsASharedPet
        => _petAccess.Values.Any(r => r.Role == "owner" && r.CarerCount > 0);

    // ── IPetAccessSource: what the billing gate reads ───────────────────────

    /// <summary>Unknown ONLY while a signed-in, backup-on device still owes its first
    /// access fetch. Every other configuration has nothing to wait for and reports true —
    /// reporting false when signed out would hold the read-only gate permanently open.</summary>
    public bool AccessKnown => _accessLoaded || !_enabled || !_auth.IsSignedIn;

    public Billing.PetAccessInfo? GetPetAccess(string? petSyncId)
    {
        if (string.IsNullOrEmpty(petSyncId))
            return null;
        // Read the field once: the sync thread swaps the whole dictionary.
        return _petAccess.TryGetValue(petSyncId, out var row)
            ? new Billing.PetAccessInfo(row.Role == "caregiver", row.OwnerAccess, row.FetchedUtc)
            : null;
    }

    public void RequestSyncSoon()
    {
        if (!_enabled || !_auth.IsSignedIn)
            return;

        // A window is already open — let it run out rather than restarting it. This is
        // called from SyncStamp.RowTouched, i.e. once per stamped row, so deleting a pet
        // with a year of history used to cancel and re-create thousands of token sources
        // and delay tasks in a tight loop. Coalescing on the existing window costs at
        // most Debounce of extra latency and allocates nothing.
        CancellationTokenSource cts;
        lock (_debounceGate)
        {
            if (_debounce != null)
                return;
            cts = _debounce = new CancellationTokenSource();
        }

        Task.Run(async () =>
        {
            try
            {
                await Task.Delay(Debounce, cts.Token);
                await SyncNowAsync();
            }
            catch (OperationCanceledException) { /* cancelled on teardown */ }
            catch (Exception ex) { Debug.WriteLine($"[Cloud] debounced sync failed: {ex.Message}"); }
            finally
            {
                // Reopen the window so the next write starts a fresh one.
                lock (_debounceGate)
                {
                    if (ReferenceEquals(_debounce, cts))
                        _debounce = null;
                }
                cts.Dispose();
            }
        }).Forget();
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

    /// <param name="retriedAfterRefresh">Set on the one retry a forced token refresh
    /// earns. It is a hard stop, not a counter: the refresh can keep succeeding while
    /// the data call keeps returning 401 (a revoked role, a skewed clock, or a plain
    /// 401 that <c>CloudHttp.Classify</c> buckets as AuthExpired), and without this the
    /// method recursed forever — unbounded requests and stack growth on a phone.</param>
    private async Task<SyncOutcome> RunOnceAsync(bool retriedAfterRefresh = false)
    {
        var session = await _auth.GetSessionAsync();
        if (session == null)
            return SyncOutcome.NotSignedIn;

        try
        {
            // Belt and braces for A3: if state from a DIFFERENT account is still here, every
            // cursor sits ahead of this account's rows and the pull would silently return
            // nothing — permanently. A proper sign-out has already torn this down; this
            // catches devices that got into the state before the teardown existed, and any
            // path that clears the session without going through it.
            await DiscardOtherAccountStateAsync(session);

            var ctx = new SyncRunContext(_db.Connection);

            // Membership first: it drives the shared-pet roles AND the purge of
            // pets whose access was revoked — RLS makes those invisible, so their
            // tombstones can never arrive; this explicit diff is the only signal.
            var changed = await SyncMembershipsAsync(session);

            // Pull before push: conflicts resolve against the freshest server
            // state, and a brand-new device naturally does a full download.
            foreach (var table in _tables)
                changed += await PullTableAsync(table, ctx, session);

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

            // Only when local data actually changed — pushes and no-op cycles
            // must not cause pointless page reloads.
            if (changed > 0)
                RemoteChangesApplied?.Invoke();

            CloudDiagnostics.Record($"[Cloud] sync OK ({changed} rows applied)");
            return SyncOutcome.Success;
        }
        catch (CloudException ex) when (ex.Kind == CloudErrorKind.Network)
        {
            CloudDiagnostics.Record("[Cloud] sync: offline");
            return SyncOutcome.Offline;
        }
        catch (CloudException ex) when (ex.Kind == CloudErrorKind.AuthExpired)
        {
            // Exactly one forced refresh; if the session is truly dead GetSessionAsync
            // clears it and the UI hears SessionChanged.
            if (!retriedAfterRefresh && await _auth.GetSessionAsync(forceRefresh: true) != null)
                return await RunOnceAsync(retriedAfterRefresh: true);

            CloudDiagnostics.Record(retriedAfterRefresh
                ? "[Cloud] sync: auth expired again immediately after a successful refresh — giving up this cycle"
                : "[Cloud] sync: auth expired, session could not be refreshed");
            return SyncOutcome.AuthExpired;
        }
        catch (Exception ex)
        {
            CloudDiagnostics.Record($"[Cloud] sync FAILED: {ex.Message}");
            return SyncOutcome.Failed;
        }
        finally
        {
            StateChanged?.Invoke();
        }
    }

    /// <summary>Fetch the caller's membership list (pet uuid → role), cache it for
    /// the sharing UI, and purge local pets whose membership is gone — the user
    /// left, was removed, or the owner deleted the pet elsewhere. Only pets that
    /// were previously pushed are candidates: a dirty pet may simply be new and
    /// gets its owner membership by being pushed later this same cycle.
    /// Returns the number of pets purged (they count as local changes).</summary>
    /// <summary>Reconcile this device's trial anchor with the account's, so the server can
    /// answer "is this owner still in their trial?" when their caregivers ask.
    ///
    /// <para>Runs only when the two disagree — the first sync after signing in, and again
    /// if the trial starts later (a caregiver who finally creates a pet of their own).
    /// <c>claim_trial_anchor</c> is set-if-earlier server-side, so this can confirm an
    /// anchor but never push one later and mint a fresh sponsorship window.</para></summary>
    private async Task ReconcileTrialAnchorAsync(CloudSession session)
    {
        try
        {
            var local = await _trialAnchor.GetStartUtcAsync();

            var doc = await _http.RpcAsync(
                "claim_trial_anchor",
                new { p_started = local is DateTime v ? CloudJson.ToIso(v) : null },
                session.AccessToken);

            DateTime? server = null;
            if (doc != null && doc.RootElement.ValueKind == JsonValueKind.String)
                server = CloudJson.ParseIso(doc.RootElement.GetString()!);

            // Keeps whichever is earlier, so this converges from either side.
            await _trialAnchor.AdoptAsync(server);
        }
        catch (Exception ex)
        {
            // The anchor is not needed to move pet data. A failure here must not abort the
            // whole cycle and leave someone unable to sync their pet's records.
            Debug.WriteLine($"[Cloud] trial anchor reconcile failed: {ex.Message}");
        }
    }

    private async Task<int> SyncMembershipsAsync(CloudSession session)
    {
        // The owner's trial anchor has to reach the server before it can answer
        // "does this owner still have access?" for their caregivers. Reconciled first so
        // this same response already reflects it.
        await ReconcileTrialAnchorAsync(session);

        var doc = await _http.RpcRequiredAsync("list_my_pet_access", new { }, session.AccessToken);
        var roles = new Dictionary<string, string>();
        var access = new Dictionary<string, PetAccessRow>();
        var now = DateTime.UtcNow;
        foreach (var el in doc.RootElement.EnumerateArray())
        {
            var petId = CloudJson.GetString(el, "pet_id");
            var role = CloudJson.GetString(el, "member_role");
            roles[petId] = role;
            access[petId] = new PetAccessRow(
                role, CloudJson.GetBool(el, "owner_access"), CloudJson.GetInt(el, "carer_count"), now);
        }

        // ── newly granted access needs a FULL pull, not an incremental one ──────
        // The pull is incremental (updated_at > cursor). Joining a pet changes what this
        // user may SEE, but it does not touch a single updated_at — every row of that pet
        // was written before we got here, so an incremental pull will never ask for any of
        // them and the caregiver ends up a member with no data.
        //
        // Resetting the cursors makes the pull that follows (same cycle, right after this
        // method) fetch everything. Occasionally redundant — pushing your own new pet also
        // looks like a gained membership — but a re-pull is idempotent (apply is LWW plus
        // natural-key matching) and a caregiver silently seeing nothing is not a failure
        // worth optimising a few requests for.
        var gained = roles.Keys.Where(k => !_petRoles.ContainsKey(k)).ToList();
        if (gained.Count > 0)
        {
            Debug.WriteLine($"[Cloud] {gained.Count} newly accessible pet(s) — resetting cursors for a full pull");
            foreach (var table in _tables)
                await _state.RemoveAsync(KeyCursorPrefix + table.CloudTable);
        }

        // Pets this user was being sponsored for a moment ago and no longer is — the owner
        // lapsed or their trial ended. Membership is intact, so the pet stays; only the
        // right to write to it went away, and that is worth saying out loud once.
        var previous = _petAccess;
        var lostSponsorship = access
            .Where(kv => kv.Value.Role == "caregiver" && !kv.Value.OwnerAccess)
            .Where(kv => previous.TryGetValue(kv.Key, out var was) && was.Role == "caregiver" && was.OwnerAccess)
            .Select(kv => kv.Key)
            .ToList();

        _petRoles = roles;
        _petAccess = access;
        _accessLoaded = true;
        await _state.SetAsync(KeyMemberships, JsonSerializer.Serialize(roles));
        // Written even when empty, so "signed in with no shared pets" resolves to KNOWN on
        // the next launch instead of leaving the gate optimistically open.
        await _state.SetAsync(KeyPetAccess, JsonSerializer.Serialize(access));

        if (lostSponsorship.Count > 0)
            SponsorshipRevoked?.Invoke(lostSponsorship);

        var purged = 0;
        var pets = await _db.Connection.QueryAsync<Pet>("select * from \"Pet\"");
        foreach (var pet in pets)
        {
            if (string.IsNullOrEmpty(pet.SyncId) || pet.IsDirty || roles.ContainsKey(pet.SyncId))
                continue;
            await PurgePetAsync(pet);
            purged++;
        }
        return purged;
    }

    /// <summary>Hard-delete a pet and everything that hangs off it from THIS
    /// device (medical data for a pet the user no longer cares for must not stay
    /// behind), cancel its reminders, and repair the active-pet selection.</summary>
    private async Task PurgePetAsync(Pet pet)
    {
        Debug.WriteLine($"[Cloud] purging pet {pet.Id} ({pet.SyncId}) — membership revoked");

        var meds = await _db.Connection.QueryAsync<Medication>(
            "select * from \"Medication\" where PetId = ?", pet.Id);

        // Children before parents, straight off the registry — MedicationSchedule's
        // predicate is a subquery over Medication, so it must run while those rows
        // still exist, which InDeletionOrder guarantees. Hard deletes, not tombstones:
        // this removal is local-only and must never propagate (see PetDeletionService
        // for the delete that does).
        await _db.Connection.RunInTransactionAsync(conn =>
        {
            foreach (var table in SyncedTables.InDeletionOrder)
                conn.Execute($"delete from \"{table.LocalTable}\" where {table.PetPredicate}", pet.Id);
        });

        // The med rows are gone, so the idempotent sync takes its cancel path
        // (notifications + pending instances).
        foreach (var med in meds)
        {
            try { await _reminders.SyncMedicationAsync(med.Id); }
            catch (Exception ex) { Debug.WriteLine($"[Cloud] purge reminder cancel {med.Id} failed: {ex.Message}"); }
        }

        // Don't leave the UI pointing at a pet that no longer exists.
        if (_activePet.ActivePet?.Id == pet.Id)
        {
            var remaining = await _db.Connection.QueryAsync<Pet>(
                "select * from \"Pet\" where IsDeleted = 0 limit 1");
            if (remaining.Count > 0)
                await _activePet.LoadActivePetAsync(remaining[0].Id);
        }
    }

    /// <summary>Returns how many rows were actually applied locally.</summary>
    private async Task<int> PullTableAsync(ITableSync table, SyncRunContext ctx, CloudSession session)
    {
        var cursorKey = KeyCursorPrefix + table.CloudTable;
        var cursor = await _state.GetAsync(cursorKey) ?? "1970-01-01T00:00:00Z";
        var applied = 0;

        while (true)
        {
            var path = $"{table.CloudTable}?select=*&order=updated_at.asc,id.asc" +
                       $"&updated_at=gt.{Uri.EscapeDataString(cursor)}&limit={PullPageSize}";
            var doc = await _http.RestGetRequiredAsync(path, session.AccessToken);
            var rows = doc.RootElement;
            var count = rows.GetArrayLength();
            if (count == 0)
                return applied;

            applied += await table.ApplyRowsAsync(ctx, rows);

            cursor = CloudJson.GetString(rows[count - 1], "updated_at");
            await _state.SetAsync(cursorKey, cursor);

            if (count < PullPageSize)
                return applied;
        }
    }

    private async Task PushTableAsync(ITableSync table, SyncRunContext ctx, CloudSession session)
    {
        var pending = await table.CollectDirtyAsync(ctx);
        for (int i = 0; i < pending.Count; i += PushBatchSize)
        {
            // GetRange, not Skip().Take() — the latter re-walks the list from the head
            // on every batch, which is quadratic across a first full backup.
            var batch = pending.GetRange(i, Math.Min(PushBatchSize, pending.Count - i));
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

        // Start a fresh anonymous analytics identity, exactly as the data reset does
        // (AppResetService). Not strictly required — events never carry an account,
        // an email, or anything personal, so there is nothing here to erase — but
        // without it this install's event stream runs unbroken across the deletion,
        // visible as one device whose account_state was signed_in and then wasn't.
        // Rotating makes "we keep nothing tied to you" true without qualification.
        //
        // Last, deliberately: SignOutAsync above flips AnalyticsContext to anonymous,
        // so the new id is never stamped signed_in. Already-queued events keep the old
        // id baked into their payloads and still send — they are anonymous and they
        // genuinely happened, so dropping them would cost telemetry for no privacy gain.
        try { Analytics.AnalyticsIdentity.Rotate(); }
        catch (Exception ex) { Debug.WriteLine($"[Cloud] analytics id rotate failed: {ex.Message}"); }
    }

    /// <summary>
    /// Drop cursors and caches left behind by a different account, and stamp the current one.
    ///
    /// <para>Cursors are a high-water mark for ONE account (<c>updated_at=gt.{cursor}</c>).
    /// Left in place across an account change they sit ahead of every row the other account
    /// owns, so the pull returns nothing and stays that way — the device holds no data and
    /// will never ask for any. Sign-out teardown normally prevents this; this repairs it.</para>
    ///
    /// <para>Deliberately does NOT re-mint <c>SyncId</c>s. That belongs to the intentional
    /// "move this device's data to a new account" flow (<see cref="EnableBackupAsync"/>);
    /// doing it on a plain sign-in would upload one account's pets into another's.</para>
    /// </summary>
    private async Task DiscardOtherAccountStateAsync(CloudSession session)
    {
        var lastAccount = await _state.GetAsync(KeyAccount);
        if (lastAccount == session.UserId)
            return;

        if (lastAccount != null)
        {
            Debug.WriteLine($"[Cloud] account changed since last sync — discarding stale cursors/caches");
            foreach (var table in _tables)
                await _state.RemoveAsync(KeyCursorPrefix + table.CloudTable);
            await _state.RemoveAsync(KeyMemberships);
            await _state.RemoveAsync(KeyPetAccess);
            _petRoles = new();
            _petAccess = new();
            _accessLoaded = false;
        }

        await _state.SetAsync(KeyAccount, session.UserId);
    }

    public async Task<SignOutImpact> PrepareSignOutAsync()
    {
        // Last chance to get outstanding work to the server. Best effort: offline is the
        // normal reason there is anything left, and it must not block signing out.
        if (_enabled && _auth.IsSignedIn)
        {
            try { await SyncNowAsync(); }
            catch (Exception ex) { Debug.WriteLine($"[Cloud] final push before sign-out failed: {ex.Message}"); }
        }

        // Count what would ACTUALLY upload, by asking the same collector the push uses —
        // not raw IsDirty flags. The two differ: a row whose parent no longer exists is
        // permanently unpushable, and counting it reports "N changes not yet saved" forever,
        // on a device where nothing is pending and another sync would not help. Warning
        // someone about work that cannot be lost (because it cannot be saved either) trains
        // them to ignore the one warning here that matters.
        var unsynced = 0;
        try
        {
            var ctx = new SyncRunContext(_db.Connection);
            foreach (var table in _tables)
                unsynced += (await table.CollectDirtyAsync(ctx)).Count;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Cloud] counting unsynced rows failed: {ex.Message}");
        }

        // Only pets this account actually holds. A pet created locally and never pushed is
        // not in the membership map, is not this account's, and is not removed — it is
        // counted above instead, because it exists nowhere else.
        var names = new List<string>();
        foreach (var pet in await _db.Connection.QueryAsync<Pet>("select * from \"Pet\""))
        {
            if (!string.IsNullOrEmpty(pet.SyncId) && _petRoles.ContainsKey(pet.SyncId))
                names.Add(pet.Name);
        }

        // A redeemed access code is held by the ACCOUNT, and its cache is cloud:-prefixed, so
        // the teardown below wipes it with everything else. Reversible on the next sign-in,
        // like the pets, but it has to be said or someone signing out for an unrelated reason
        // quietly loses a year they were given.
        return new SignOutImpact(names, unsynced, _grants.IsGranted);
    }

    public async Task<int> SignOutTeardownAsync()
    {
        // Data follows access. Signing out of an account is losing access to its pets, and
        // the app already commits to "medical data for a pet you no longer care for never
        // stays behind" for revoked caregivers — same rule, same mechanism (reminders
        // cancelled, active-pet selection repaired).
        foreach (var pet in await _db.Connection.QueryAsync<Pet>("select * from \"Pet\""))
        {
            if (!string.IsNullOrEmpty(pet.SyncId) && _petRoles.ContainsKey(pet.SyncId))
                await PurgePetAsync(pet);
        }

        // Everything account-scoped, in one call that a future key cannot escape. NB the
        // trial anchor itself lives in AppSettings and is DEVICE-scoped: clearing it here
        // would hand out a fresh 14-day trial on every sign-out. Only the reconciliation
        // marker (cloud:trialAnchor) is account-scoped, and it goes with this prefix.
        await _state.ClearPrefixAsync(CloudStatePrefix);

        _enabled = false;
        _lastSynced = null;
        _petRoles = new();
        _petAccess = new();
        _accessLoaded = false;
        StateChanged?.Invoke();

        return await _db.Connection.ExecuteScalarAsync<int>(
            "select count(*) from \"Pet\" where IsDeleted = 0");
    }

    /// <summary>Queue every active row for upload (tombstones stay local noise).</summary>
    private async Task MarkAllDirtyAsync()
    {
        foreach (var t in SyncedTables.LocalTableNames)
            await _db.Connection.ExecuteAsync($"update \"{t}\" set IsDirty = 1 where IsDeleted = 0");
    }

    /// <summary>Fresh GUIDs for every active row + cleared cursors, so a switch to
    /// a new account uploads clean copies instead of colliding with rows owned by
    /// the previous account.</summary>
    private async Task ResetSyncIdentityAsync()
    {
        await _db.Connection.RunInTransactionAsync(conn =>
        {
            foreach (var t in SyncedTables.LocalTableNames)
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
}
