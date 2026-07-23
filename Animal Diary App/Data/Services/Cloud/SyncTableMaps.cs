namespace Animal_Diary_App.Data.Services.Cloud;

using System.Diagnostics;
using System.Text.Json;
using Animal_Diary_App.Data.Models;
using SQLite;

/// <summary>
/// Everything the sync engine shares with one table's mapping while a run is in
/// flight: the connection, the local↔cloud id caches (local FKs are ints, cloud
/// FKs are the parents' SyncIds), and the set of medications whose reminders
/// must re-materialize after the pull.
/// </summary>
internal sealed class SyncRunContext
{
    public SyncRunContext(SQLiteAsyncConnection db) { Db = db; }

    public SQLiteAsyncConnection Db { get; }

    /// <summary>Medication local ids touched by this run's pull — the engine runs
    /// the idempotent reminder re-sync for each once the pull is applied.</summary>
    public HashSet<int> AffectedMedications { get; } = new();

    private readonly Dictionary<int, string?> _petUuid = new();
    private readonly Dictionary<string, int?> _petLocal = new();
    private readonly Dictionary<int, string?> _medUuid = new();
    private readonly Dictionary<string, int?> _medLocal = new();

    public async Task<string?> PetUuidAsync(int localId)
    {
        if (!_petUuid.TryGetValue(localId, out var v))
            _petUuid[localId] = v = (await Db.QueryAsync<Pet>(
                "select * from \"Pet\" where Id = ? limit 1", localId)).FirstOrDefault()?.SyncId;
        return string.IsNullOrEmpty(v) ? null : v;
    }

    public async Task<int?> PetLocalIdAsync(string uuid)
    {
        if (!_petLocal.TryGetValue(uuid, out var v))
            _petLocal[uuid] = v = (await Db.QueryAsync<Pet>(
                "select * from \"Pet\" where SyncId = ? limit 1", uuid)).FirstOrDefault()?.Id;
        return v;
    }

    public async Task<string?> MedicationUuidAsync(int localId)
    {
        if (!_medUuid.TryGetValue(localId, out var v))
            _medUuid[localId] = v = (await Db.QueryAsync<Medication>(
                "select * from \"Medication\" where Id = ? limit 1", localId)).FirstOrDefault()?.SyncId;
        return string.IsNullOrEmpty(v) ? null : v;
    }

    public async Task<int?> MedicationLocalIdAsync(string uuid)
    {
        if (!_medLocal.TryGetValue(uuid, out var v))
            _medLocal[uuid] = v = (await Db.QueryAsync<Medication>(
                "select * from \"Medication\" where SyncId = ? limit 1", uuid)).FirstOrDefault()?.Id;
        return v;
    }

    /// <summary>A pet applied in this run may be looked up by children later in the
    /// same run — prime the cache instead of re-querying.</summary>
    public void NotePet(int localId, string uuid) { _petLocal[uuid] = localId; _petUuid[localId] = uuid; }
    public void NoteMedication(int localId, string uuid) { _medLocal[uuid] = localId; _medUuid[localId] = uuid; }
}

/// <summary>One row queued for upload; <c>ClearDirtyAsync</c> runs only after the
/// server accepted the batch, and only clears the flag if the row wasn't written
/// again mid-push (snapshot comparison).</summary>
internal sealed record PendingPush(
    Dictionary<string, object?> Payload,
    Func<Task> ClearDirtyAsync);

internal interface ITableSync
{
    string CloudTable { get; }
    Task<int> ApplyRowsAsync(SyncRunContext ctx, JsonElement rows);
    Task<List<PendingPush>> CollectDirtyAsync(SyncRunContext ctx);
}

/// <summary>
/// The generic pull-apply / collect-dirty machinery for one entity type; the
/// per-table differences (column mapping, FK resolution, natural keys) are the
/// delegates. Apply rules:
/// - match local by SyncId, else by the table's natural key (that's how two
///   devices' "same" row converges — the local row adopts the canonical id);
/// - a locally-dirty row that is strictly newer wins and pushes later (LWW);
/// - applied writes never mark dirty (they ARE the server state).
/// </summary>
internal sealed class TableSync<T> : ITableSync where T : class, ISyncable, new()
{
    private readonly string _localTable = typeof(T).Name;
    private readonly Func<T, SyncRunContext, Task<Dictionary<string, object?>?>> _toCloud;
    private readonly Func<JsonElement, SyncRunContext, Task<T?>> _fromCloud;
    private readonly Action<T, T> _copyPayload;
    private readonly Func<SyncRunContext, T, Task<T?>>? _naturalKey;
    private readonly Action<SyncRunContext, T>? _onApplied;

    public string CloudTable { get; }

    public TableSync(
        string cloudTable,
        Func<T, SyncRunContext, Task<Dictionary<string, object?>?>> toCloud,
        Func<JsonElement, SyncRunContext, Task<T?>> fromCloud,
        Action<T, T> copyPayload,
        Func<SyncRunContext, T, Task<T?>>? naturalKey = null,
        Action<SyncRunContext, T>? onApplied = null)
    {
        CloudTable = cloudTable;
        _toCloud = toCloud;
        _fromCloud = fromCloud;
        _copyPayload = copyPayload;
        _naturalKey = naturalKey;
        _onApplied = onApplied;
    }

    public async Task<int> ApplyRowsAsync(SyncRunContext ctx, JsonElement rows)
    {
        int applied = 0;
        foreach (var el in rows.EnumerateArray())
        {
            var incoming = await _fromCloud(el, ctx);
            if (incoming == null)
            {
                // Unresolvable parent — parents sync first, so this is exceptional;
                // the row returns when its updated_at moves. Log, don't crash.
                Debug.WriteLine($"[Cloud] skipped {CloudTable} row with unresolved FK");
                continue;
            }

            var local = (await ctx.Db.QueryAsync<T>(
                $"select * from \"{_localTable}\" where SyncId = ? limit 1", incoming.SyncId)).FirstOrDefault();
            if (local == null && _naturalKey != null)
                local = await _naturalKey(ctx, incoming);

            T persisted;
            if (local == null)
            {
                if (incoming.IsDeleted)
                    continue;                     // tombstone for a row this device never had
                await ctx.Db.InsertAsync(incoming);
                persisted = incoming;
                applied++;
            }
            else
            {
                if (local.IsDirty && local.UpdatedAtUtc > incoming.UpdatedAtUtc)
                    continue;                     // local edit is newer; it pushes next
                if (!local.IsDirty && local.SyncId == incoming.SyncId &&
                    local.UpdatedAtUtc == incoming.UpdatedAtUtc && local.IsDeleted == incoming.IsDeleted)
                    continue;                     // echo of a row we already hold (our own push coming back)
                _copyPayload(local, incoming);
                local.SyncId = incoming.SyncId;   // natural-key merge adopts the canonical id
                local.UpdatedAtUtc = incoming.UpdatedAtUtc;
                local.IsDeleted = incoming.IsDeleted;
                local.IsDirty = false;
                await ctx.Db.UpdateAsync(local);
                persisted = local;
                applied++;
            }
            _onApplied?.Invoke(ctx, persisted);
        }
        return applied;
    }

    public async Task<List<PendingPush>> CollectDirtyAsync(SyncRunContext ctx)
    {
        var rows = await ctx.Db.QueryAsync<T>($"select * from \"{_localTable}\" where IsDirty = 1");
        var result = new List<PendingPush>();
        foreach (var row in rows)
        {
            var payload = await _toCloud(row, ctx);
            if (payload == null)
                continue;                          // parent has no cloud identity yet

            var snapshotId = row.Id;
            var snapshotStamp = row.UpdatedAtUtc;
            result.Add(new PendingPush(payload, async () =>
            {
                var current = (await ctx.Db.QueryAsync<T>(
                    $"select * from \"{_localTable}\" where Id = ? limit 1", snapshotId)).FirstOrDefault();
                // Only clear if the row wasn't written again while the push flew.
                if (current != null && current.IsDirty && current.UpdatedAtUtc == snapshotStamp)
                {
                    current.IsDirty = false;
                    await ctx.Db.UpdateAsync(current);
                }
            }));
        }
        return result;
    }
}

/// <summary>The ten table mappings, in dependency order (parents before children —
/// both pull and push walk this order so FKs always resolve).</summary>
internal static class SyncTableMaps
{
    public static IReadOnlyList<ITableSync> Build() => new ITableSync[]
    {
        // ── pets (root) ──────────────────────────────────────────────────────
        new TableSync<Pet>("pets",
            toCloud: (p, _) => Task.FromResult<Dictionary<string, object?>?>(new()
            {
                ["id"] = p.SyncId,
                ["name"] = p.Name,
                ["type"] = p.Type,
                ["age"] = p.Age,
                ["birth_year"] = p.BirthYear,
                ["birth_month"] = p.BirthMonth,
                ["birth_day"] = p.BirthDay,
                ["condition_id"] = p.ConditionId,
                ["client_updated_at"] = CloudJson.ToIso(p.UpdatedAtUtc),
                ["deleted_at"] = p.IsDeleted ? CloudJson.ToIso(p.UpdatedAtUtc) : null,
            }),
            fromCloud: (el, _) => Task.FromResult<Pet?>(new Pet
            {
                SyncId = CloudJson.GetString(el, "id"),
                Name = CloudJson.GetString(el, "name"),
                Type = CloudJson.GetString(el, "type"),
                Age = CloudJson.GetInt(el, "age"),
                BirthYear = CloudJson.GetInt(el, "birth_year"),
                BirthMonth = CloudJson.GetIntOrNull(el, "birth_month"),
                BirthDay = CloudJson.GetIntOrNull(el, "birth_day"),
                ConditionId = CloudJson.GetString(el, "condition_id"),
                UpdatedAtUtc = CloudJson.GetIsoDateTime(el, "client_updated_at"),
                IsDeleted = CloudJson.IsDeleted(el),
            }),
            copyPayload: (local, inc) =>
            {
                local.Name = inc.Name; local.Type = inc.Type; local.Age = inc.Age;
                local.BirthYear = inc.BirthYear; local.BirthMonth = inc.BirthMonth;
                local.BirthDay = inc.BirthDay; local.ConditionId = inc.ConditionId;
            },
            onApplied: (ctx, p) => ctx.NotePet(p.Id, p.SyncId)),

        // ── medications ──────────────────────────────────────────────────────
        new TableSync<Medication>("medications",
            toCloud: async (m, ctx) =>
            {
                var petUuid = await ctx.PetUuidAsync(m.PetId);
                if (petUuid == null) return null;
                return new()
                {
                    ["id"] = m.SyncId,
                    ["pet_id"] = petUuid,
                    ["name"] = m.Name,
                    ["dosage"] = m.Dosage,
                    ["unit"] = m.Unit,
                    ["notes"] = m.Notes,
                    ["is_archived"] = m.IsArchived,
                    ["med_created_at"] = CloudJson.ToIso(m.CreatedAt),
                    ["client_updated_at"] = CloudJson.ToIso(m.UpdatedAtUtc),
                    ["deleted_at"] = m.IsDeleted ? CloudJson.ToIso(m.UpdatedAtUtc) : null,
                };
            },
            fromCloud: async (el, ctx) =>
            {
                var petId = await ctx.PetLocalIdAsync(CloudJson.GetString(el, "pet_id"));
                if (petId == null) return null;
                return new Medication
                {
                    SyncId = CloudJson.GetString(el, "id"),
                    PetId = petId.Value,
                    Name = CloudJson.GetString(el, "name"),
                    Dosage = CloudJson.GetDecimal(el, "dosage"),
                    Unit = CloudJson.GetString(el, "unit"),
                    Notes = CloudJson.GetString(el, "notes"),
                    IsArchived = CloudJson.GetBool(el, "is_archived"),
                    CreatedAt = CloudJson.GetIsoDateTimeOrNull(el, "med_created_at") ?? DateTime.MinValue,
                    UpdatedAtUtc = CloudJson.GetIsoDateTime(el, "client_updated_at"),
                    IsDeleted = CloudJson.IsDeleted(el),
                };
            },
            copyPayload: (local, inc) =>
            {
                local.PetId = inc.PetId; local.Name = inc.Name; local.Dosage = inc.Dosage;
                local.Unit = inc.Unit; local.Notes = inc.Notes; local.IsArchived = inc.IsArchived;
                local.CreatedAt = inc.CreatedAt;
            },
            onApplied: (ctx, m) => { ctx.NoteMedication(m.Id, m.SyncId); ctx.AffectedMedications.Add(m.Id); }),

        // ── trackers (care plan; one per kind per pet) ───────────────────────
        new TableSync<Tracker>("trackers",
            toCloud: async (t, ctx) =>
            {
                var petUuid = await ctx.PetUuidAsync(t.PetId);
                if (petUuid == null) return null;
                return new()
                {
                    ["id"] = t.SyncId,
                    ["pet_id"] = petUuid,
                    ["tracker_id"] = t.TrackerId.ToString(),
                    ["kind"] = t.Kind.ToString(),
                    ["per_day_count"] = t.PerDayCount,
                    ["target_lo"] = t.TargetLo,
                    ["target_hi"] = t.TargetHi,
                    ["unit"] = t.Unit,
                    ["from_condition"] = t.FromCondition,
                    ["client_updated_at"] = CloudJson.ToIso(t.UpdatedAtUtc),
                    ["deleted_at"] = t.IsDeleted ? CloudJson.ToIso(t.UpdatedAtUtc) : null,
                };
            },
            fromCloud: async (el, ctx) =>
            {
                var petId = await ctx.PetLocalIdAsync(CloudJson.GetString(el, "pet_id"));
                if (petId == null) return null;
                return new Tracker
                {
                    SyncId = CloudJson.GetString(el, "id"),
                    PetId = petId.Value,
                    TrackerId = Enum.Parse<TrackerId>(CloudJson.GetString(el, "tracker_id")),
                    Kind = Enum.Parse<TrackerKind>(CloudJson.GetString(el, "kind")),
                    PerDayCount = CloudJson.GetInt(el, "per_day_count"),
                    TargetLo = CloudJson.GetDecimalOrNull(el, "target_lo"),
                    TargetHi = CloudJson.GetDecimalOrNull(el, "target_hi"),
                    Unit = CloudJson.GetString(el, "unit"),
                    FromCondition = CloudJson.GetStringOrNull(el, "from_condition"),
                    UpdatedAtUtc = CloudJson.GetIsoDateTime(el, "client_updated_at"),
                    IsDeleted = CloudJson.IsDeleted(el),
                };
            },
            copyPayload: (local, inc) =>
            {
                local.PetId = inc.PetId; local.TrackerId = inc.TrackerId; local.Kind = inc.Kind;
                local.PerDayCount = inc.PerDayCount; local.TargetLo = inc.TargetLo;
                local.TargetHi = inc.TargetHi; local.Unit = inc.Unit; local.FromCondition = inc.FromCondition;
            },
            naturalKey: async (ctx, inc) => (await ctx.Db.QueryAsync<Tracker>(
                "select * from \"Tracker\" where PetId = ? and TrackerId = ? order by IsDeleted asc limit 1",
                inc.PetId, inc.TrackerId.ToString())).FirstOrDefault()),

        // ── pet conditions ───────────────────────────────────────────────────
        new TableSync<PetCondition>("pet_conditions",
            toCloud: async (c, ctx) =>
            {
                var petUuid = await ctx.PetUuidAsync(c.PetId);
                if (petUuid == null) return null;
                return new()
                {
                    ["id"] = c.SyncId,
                    ["pet_id"] = petUuid,
                    ["condition_id"] = c.ConditionId,
                    ["client_updated_at"] = CloudJson.ToIso(c.UpdatedAtUtc),
                    ["deleted_at"] = c.IsDeleted ? CloudJson.ToIso(c.UpdatedAtUtc) : null,
                };
            },
            fromCloud: async (el, ctx) =>
            {
                var petId = await ctx.PetLocalIdAsync(CloudJson.GetString(el, "pet_id"));
                if (petId == null) return null;
                return new PetCondition
                {
                    SyncId = CloudJson.GetString(el, "id"),
                    PetId = petId.Value,
                    ConditionId = CloudJson.GetString(el, "condition_id"),
                    UpdatedAtUtc = CloudJson.GetIsoDateTime(el, "client_updated_at"),
                    IsDeleted = CloudJson.IsDeleted(el),
                };
            },
            copyPayload: (local, inc) => { local.PetId = inc.PetId; local.ConditionId = inc.ConditionId; },
            naturalKey: async (ctx, inc) => (await ctx.Db.QueryAsync<PetCondition>(
                "select * from \"PetCondition\" where PetId = ? and ConditionId = ? order by IsDeleted asc limit 1",
                inc.PetId, inc.ConditionId)).FirstOrDefault()),

        // ── pet entries (mood + weight; one row per pet per day) ─────────────
        new TableSync<PetEntry>("pet_entries",
            toCloud: async (e, ctx) =>
            {
                var petUuid = await ctx.PetUuidAsync(e.PetId);
                if (petUuid == null) return null;
                return new()
                {
                    ["id"] = e.SyncId,
                    ["pet_id"] = petUuid,
                    ["entry_date"] = CloudJson.ToDateOnly(e.Date),
                    ["mood"] = e.Mood,
                    ["mood_level"] = e.MoodLevel,
                    ["mood_note"] = e.MoodNote,
                    ["include_in_vet_report"] = e.IncludeInVetReport,
                    ["weight"] = e.Weight,
                    ["mood_time_ticks"] = e.MoodTimeTicks,
                    ["weight_time_ticks"] = e.WeightTimeTicks,
                    ["client_updated_at"] = CloudJson.ToIso(e.UpdatedAtUtc),
                    ["deleted_at"] = e.IsDeleted ? CloudJson.ToIso(e.UpdatedAtUtc) : null,
                };
            },
            fromCloud: async (el, ctx) =>
            {
                var petId = await ctx.PetLocalIdAsync(CloudJson.GetString(el, "pet_id"));
                if (petId == null) return null;
                return new PetEntry
                {
                    SyncId = CloudJson.GetString(el, "id"),
                    PetId = petId.Value,
                    Date = CloudJson.ParseDateOnly(CloudJson.GetString(el, "entry_date")),
                    Mood = CloudJson.GetString(el, "mood"),
                    MoodLevel = CloudJson.GetInt(el, "mood_level"),
                    MoodNote = CloudJson.GetString(el, "mood_note"),
                    IncludeInVetReport = CloudJson.GetBool(el, "include_in_vet_report"),
                    Weight = CloudJson.GetDecimal(el, "weight"),
                    MoodTimeTicks = CloudJson.GetLongOrNull(el, "mood_time_ticks"),
                    WeightTimeTicks = CloudJson.GetLongOrNull(el, "weight_time_ticks"),
                    UpdatedAtUtc = CloudJson.GetIsoDateTime(el, "client_updated_at"),
                    IsDeleted = CloudJson.IsDeleted(el),
                };
            },
            copyPayload: (local, inc) =>
            {
                local.PetId = inc.PetId; local.Date = inc.Date; local.Mood = inc.Mood;
                local.MoodLevel = inc.MoodLevel; local.MoodNote = inc.MoodNote;
                local.IncludeInVetReport = inc.IncludeInVetReport; local.Weight = inc.Weight;
                local.MoodTimeTicks = inc.MoodTimeTicks; local.WeightTimeTicks = inc.WeightTimeTicks;
            },
            naturalKey: async (ctx, inc) => (await ctx.Db.QueryAsync<PetEntry>(
                "select * from \"PetEntry\" where PetId = ? and Date = ? order by IsDeleted asc limit 1",
                inc.PetId, inc.Date)).FirstOrDefault()),

        // ── glucose (append-only events) ─────────────────────────────────────
        new TableSync<GlucoseEntry>("glucose_entries",
            toCloud: async (g, ctx) =>
            {
                var petUuid = await ctx.PetUuidAsync(g.PetId);
                if (petUuid == null) return null;
                return new()
                {
                    ["id"] = g.SyncId,
                    ["pet_id"] = petUuid,
                    ["entry_date"] = CloudJson.ToDateOnly(g.Date),
                    ["time_ticks"] = g.Time.Ticks,
                    ["value"] = g.Value,
                    ["food_context"] = g.Context.ToString(),
                    ["client_updated_at"] = CloudJson.ToIso(g.UpdatedAtUtc),
                    ["deleted_at"] = g.IsDeleted ? CloudJson.ToIso(g.UpdatedAtUtc) : null,
                };
            },
            fromCloud: async (el, ctx) =>
            {
                var petId = await ctx.PetLocalIdAsync(CloudJson.GetString(el, "pet_id"));
                if (petId == null) return null;
                return new GlucoseEntry
                {
                    SyncId = CloudJson.GetString(el, "id"),
                    PetId = petId.Value,
                    Date = CloudJson.ParseDateOnly(CloudJson.GetString(el, "entry_date")),
                    Time = CloudJson.GetTicksTime(el, "time_ticks"),
                    Value = CloudJson.GetDecimal(el, "value"),
                    Context = Enum.Parse<FoodContext>(CloudJson.GetString(el, "food_context")),
                    UpdatedAtUtc = CloudJson.GetIsoDateTime(el, "client_updated_at"),
                    IsDeleted = CloudJson.IsDeleted(el),
                };
            },
            copyPayload: (local, inc) =>
            {
                local.PetId = inc.PetId; local.Date = inc.Date; local.Time = inc.Time;
                local.Value = inc.Value; local.Context = inc.Context;
            }),

        // ── appetite (one row per pet per day) ───────────────────────────────
        new TableSync<AppetiteEntry>("appetite_entries",
            toCloud: async (a, ctx) =>
            {
                var petUuid = await ctx.PetUuidAsync(a.PetId);
                if (petUuid == null) return null;
                return new()
                {
                    ["id"] = a.SyncId,
                    ["pet_id"] = petUuid,
                    ["entry_date"] = CloudJson.ToDateOnly(a.Date),
                    ["time_ticks"] = a.Time.Ticks,
                    ["level"] = a.Level,
                    ["food"] = a.Food,
                    ["client_updated_at"] = CloudJson.ToIso(a.UpdatedAtUtc),
                    ["deleted_at"] = a.IsDeleted ? CloudJson.ToIso(a.UpdatedAtUtc) : null,
                };
            },
            fromCloud: async (el, ctx) =>
            {
                var petId = await ctx.PetLocalIdAsync(CloudJson.GetString(el, "pet_id"));
                if (petId == null) return null;
                return new AppetiteEntry
                {
                    SyncId = CloudJson.GetString(el, "id"),
                    PetId = petId.Value,
                    Date = CloudJson.ParseDateOnly(CloudJson.GetString(el, "entry_date")),
                    Time = CloudJson.GetTicksTime(el, "time_ticks"),
                    Level = CloudJson.GetInt(el, "level"),
                    Food = CloudJson.GetString(el, "food"),
                    UpdatedAtUtc = CloudJson.GetIsoDateTime(el, "client_updated_at"),
                    IsDeleted = CloudJson.IsDeleted(el),
                };
            },
            copyPayload: (local, inc) =>
            {
                local.PetId = inc.PetId; local.Date = inc.Date; local.Time = inc.Time;
                local.Level = inc.Level; local.Food = inc.Food;
            },
            naturalKey: async (ctx, inc) => (await ctx.Db.QueryAsync<AppetiteEntry>(
                "select * from \"AppetiteEntry\" where PetId = ? and Date = ? order by IsDeleted asc limit 1",
                inc.PetId, inc.Date)).FirstOrDefault()),

        // ── appetite amounts (exact grams; additive events, keyed by id) ─────
        new TableSync<AppetiteAmountEntry>("appetite_amount_entries",
            toCloud: async (a, ctx) =>
            {
                var petUuid = await ctx.PetUuidAsync(a.PetId);
                if (petUuid == null) return null;
                return new()
                {
                    ["id"] = a.SyncId,
                    ["pet_id"] = petUuid,
                    ["entry_date"] = CloudJson.ToDateOnly(a.Date),
                    ["time_ticks"] = a.Time.Ticks,
                    ["grams"] = a.Grams,
                    ["food"] = a.Food,
                    ["client_updated_at"] = CloudJson.ToIso(a.UpdatedAtUtc),
                    ["deleted_at"] = a.IsDeleted ? CloudJson.ToIso(a.UpdatedAtUtc) : null,
                };
            },
            fromCloud: async (el, ctx) =>
            {
                var petId = await ctx.PetLocalIdAsync(CloudJson.GetString(el, "pet_id"));
                if (petId == null) return null;
                return new AppetiteAmountEntry
                {
                    SyncId = CloudJson.GetString(el, "id"),
                    PetId = petId.Value,
                    Date = CloudJson.ParseDateOnly(CloudJson.GetString(el, "entry_date")),
                    Time = CloudJson.GetTicksTime(el, "time_ticks"),
                    Grams = CloudJson.GetDecimal(el, "grams"),
                    Food = CloudJson.GetString(el, "food"),
                    UpdatedAtUtc = CloudJson.GetIsoDateTime(el, "client_updated_at"),
                    IsDeleted = CloudJson.IsDeleted(el),
                };
            },
            copyPayload: (local, inc) =>
            {
                local.PetId = inc.PetId; local.Date = inc.Date; local.Time = inc.Time;
                local.Grams = inc.Grams; local.Food = inc.Food;
            }),

        // ── water amounts (exact ml; additive events, keyed by id like glucose) ──
        new TableSync<WaterAmountEntry>("water_amount_entries",
            toCloud: async (w, ctx) =>
            {
                var petUuid = await ctx.PetUuidAsync(w.PetId);
                if (petUuid == null) return null;
                return new()
                {
                    ["id"] = w.SyncId,
                    ["pet_id"] = petUuid,
                    ["entry_date"] = CloudJson.ToDateOnly(w.Date),
                    ["time_ticks"] = w.Time.Ticks,
                    ["amount_ml"] = w.AmountMl,
                    ["client_updated_at"] = CloudJson.ToIso(w.UpdatedAtUtc),
                    ["deleted_at"] = w.IsDeleted ? CloudJson.ToIso(w.UpdatedAtUtc) : null,
                };
            },
            fromCloud: async (el, ctx) =>
            {
                var petId = await ctx.PetLocalIdAsync(CloudJson.GetString(el, "pet_id"));
                if (petId == null) return null;
                return new WaterAmountEntry
                {
                    SyncId = CloudJson.GetString(el, "id"),
                    PetId = petId.Value,
                    Date = CloudJson.ParseDateOnly(CloudJson.GetString(el, "entry_date")),
                    Time = CloudJson.GetTicksTime(el, "time_ticks"),
                    AmountMl = CloudJson.GetDecimal(el, "amount_ml"),
                    UpdatedAtUtc = CloudJson.GetIsoDateTime(el, "client_updated_at"),
                    IsDeleted = CloudJson.IsDeleted(el),
                };
            },
            copyPayload: (local, inc) =>
            {
                local.PetId = inc.PetId; local.Date = inc.Date; local.Time = inc.Time;
                local.AmountMl = inc.AmountMl;
            }),

        // ── water level (relative; one row per pet per day, like appetite) ───
        new TableSync<WaterLevelEntry>("water_level_entries",
            toCloud: async (w, ctx) =>
            {
                var petUuid = await ctx.PetUuidAsync(w.PetId);
                if (petUuid == null) return null;
                return new()
                {
                    ["id"] = w.SyncId,
                    ["pet_id"] = petUuid,
                    ["entry_date"] = CloudJson.ToDateOnly(w.Date),
                    ["time_ticks"] = w.Time.Ticks,
                    ["level"] = w.Level,
                    ["client_updated_at"] = CloudJson.ToIso(w.UpdatedAtUtc),
                    ["deleted_at"] = w.IsDeleted ? CloudJson.ToIso(w.UpdatedAtUtc) : null,
                };
            },
            fromCloud: async (el, ctx) =>
            {
                var petId = await ctx.PetLocalIdAsync(CloudJson.GetString(el, "pet_id"));
                if (petId == null) return null;
                return new WaterLevelEntry
                {
                    SyncId = CloudJson.GetString(el, "id"),
                    PetId = petId.Value,
                    Date = CloudJson.ParseDateOnly(CloudJson.GetString(el, "entry_date")),
                    Time = CloudJson.GetTicksTime(el, "time_ticks"),
                    Level = CloudJson.GetInt(el, "level"),
                    UpdatedAtUtc = CloudJson.GetIsoDateTime(el, "client_updated_at"),
                    IsDeleted = CloudJson.IsDeleted(el),
                };
            },
            copyPayload: (local, inc) =>
            {
                local.PetId = inc.PetId; local.Date = inc.Date; local.Time = inc.Time; local.Level = inc.Level;
            },
            naturalKey: async (ctx, inc) => (await ctx.Db.QueryAsync<WaterLevelEntry>(
                "select * from \"WaterLevelEntry\" where PetId = ? and Date = ? order by IsDeleted asc limit 1",
                inc.PetId, inc.Date)).FirstOrDefault()),

        // ── seizures (append-only events) ────────────────────────────────────
        new TableSync<SeizureEntry>("seizure_entries",
            toCloud: async (s, ctx) =>
            {
                var petUuid = await ctx.PetUuidAsync(s.PetId);
                if (petUuid == null) return null;
                return new()
                {
                    ["id"] = s.SyncId,
                    ["pet_id"] = petUuid,
                    ["entry_date"] = CloudJson.ToDateOnly(s.Date),
                    ["time_ticks"] = s.Time.Ticks,
                    ["duration_minutes"] = s.DurationMinutes,
                    ["note"] = s.Note,
                    ["client_updated_at"] = CloudJson.ToIso(s.UpdatedAtUtc),
                    ["deleted_at"] = s.IsDeleted ? CloudJson.ToIso(s.UpdatedAtUtc) : null,
                };
            },
            fromCloud: async (el, ctx) =>
            {
                var petId = await ctx.PetLocalIdAsync(CloudJson.GetString(el, "pet_id"));
                if (petId == null) return null;
                return new SeizureEntry
                {
                    SyncId = CloudJson.GetString(el, "id"),
                    PetId = petId.Value,
                    Date = CloudJson.ParseDateOnly(CloudJson.GetString(el, "entry_date")),
                    Time = CloudJson.GetTicksTime(el, "time_ticks"),
                    DurationMinutes = CloudJson.GetIntOrNull(el, "duration_minutes"),
                    Note = CloudJson.GetString(el, "note"),
                    UpdatedAtUtc = CloudJson.GetIsoDateTime(el, "client_updated_at"),
                    IsDeleted = CloudJson.IsDeleted(el),
                };
            },
            copyPayload: (local, inc) =>
            {
                local.PetId = inc.PetId; local.Date = inc.Date; local.Time = inc.Time;
                local.DurationMinutes = inc.DurationMinutes; local.Note = inc.Note;
            }),

        // ── medication schedules (replace-set rows; keyed by id) ─────────────
        new TableSync<MedicationSchedule>("medication_schedules",
            toCloud: async (s, ctx) =>
            {
                var medUuid = await ctx.MedicationUuidAsync(s.MedicationId);
                if (medUuid == null) return null;
                return new()
                {
                    ["id"] = s.SyncId,
                    ["medication_id"] = medUuid,
                    ["day_of_week"] = (int)s.Day,
                    ["time_ticks"] = s.Time.Ticks,
                    ["client_updated_at"] = CloudJson.ToIso(s.UpdatedAtUtc),
                    ["deleted_at"] = s.IsDeleted ? CloudJson.ToIso(s.UpdatedAtUtc) : null,
                };
            },
            fromCloud: async (el, ctx) =>
            {
                var medId = await ctx.MedicationLocalIdAsync(CloudJson.GetString(el, "medication_id"));
                if (medId == null) return null;
                return new MedicationSchedule
                {
                    SyncId = CloudJson.GetString(el, "id"),
                    MedicationId = medId.Value,
                    Day = (DayOfWeek)CloudJson.GetInt(el, "day_of_week"),
                    Time = CloudJson.GetTicksTime(el, "time_ticks"),
                    UpdatedAtUtc = CloudJson.GetIsoDateTime(el, "client_updated_at"),
                    IsDeleted = CloudJson.IsDeleted(el),
                };
            },
            copyPayload: (local, inc) =>
            {
                local.MedicationId = inc.MedicationId; local.Day = inc.Day; local.Time = inc.Time;
            },
            onApplied: (ctx, s) => ctx.AffectedMedications.Add(s.MedicationId)),

        // ── dose logs (adherence; keyed by medication+date+time) ─────────────
        new TableSync<MedicationDoseLog>("medication_dose_logs",
            toCloud: async (l, ctx) =>
            {
                var medUuid = await ctx.MedicationUuidAsync(l.MedicationId);
                var petUuid = await ctx.PetUuidAsync(l.PetId);
                if (medUuid == null || petUuid == null) return null;
                return new()
                {
                    ["id"] = l.SyncId,
                    ["medication_id"] = medUuid,
                    ["pet_id"] = petUuid,
                    ["scheduled_date"] = CloudJson.ToDateOnly(l.ScheduledDate),
                    ["scheduled_time_ticks"] = l.ScheduledTime.Ticks,
                    ["status"] = l.Status.ToString(),
                    ["resolved_at"] = l.ResolvedAt is DateTime r ? CloudJson.ToIso(r) : null,
                    ["client_updated_at"] = CloudJson.ToIso(l.UpdatedAtUtc),
                    ["deleted_at"] = l.IsDeleted ? CloudJson.ToIso(l.UpdatedAtUtc) : null,
                };
            },
            fromCloud: async (el, ctx) =>
            {
                var medId = await ctx.MedicationLocalIdAsync(CloudJson.GetString(el, "medication_id"));
                var petId = await ctx.PetLocalIdAsync(CloudJson.GetString(el, "pet_id"));
                if (medId == null || petId == null) return null;
                return new MedicationDoseLog
                {
                    SyncId = CloudJson.GetString(el, "id"),
                    MedicationId = medId.Value,
                    PetId = petId.Value,
                    ScheduledDate = CloudJson.ParseDateOnly(CloudJson.GetString(el, "scheduled_date")),
                    ScheduledTime = CloudJson.GetTicksTime(el, "scheduled_time_ticks"),
                    Status = Enum.Parse<DoseStatus>(CloudJson.GetString(el, "status")),
                    ResolvedAt = CloudJson.GetIsoDateTimeOrNull(el, "resolved_at"),
                    UpdatedAtUtc = CloudJson.GetIsoDateTime(el, "client_updated_at"),
                    IsDeleted = CloudJson.IsDeleted(el),
                };
            },
            copyPayload: (local, inc) =>
            {
                local.MedicationId = inc.MedicationId; local.PetId = inc.PetId;
                local.ScheduledDate = inc.ScheduledDate; local.ScheduledTime = inc.ScheduledTime;
                local.Status = inc.Status; local.ResolvedAt = inc.ResolvedAt;
            },
            naturalKey: async (ctx, inc) => (await ctx.Db.QueryAsync<MedicationDoseLog>(
                "select * from \"MedicationDoseLog\" where MedicationId = ? and ScheduledDate = ? and ScheduledTime = ? order by IsDeleted asc limit 1",
                inc.MedicationId, inc.ScheduledDate, inc.ScheduledTime)).FirstOrDefault(),
            onApplied: (ctx, l) => ctx.AffectedMedications.Add(l.MedicationId)),
    };
}
