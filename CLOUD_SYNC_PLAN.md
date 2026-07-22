# Cloud Backup & Multi-Caregiver Sync — Implementation Plan

> Status: **proposal / plan** — nothing in this document is built yet.
> Scope: optional cloud backup + multi-caregiver synchronization for Felova,
> preserving the local-first architecture. Users who never enable cloud features
> see zero change: no account, no network, SQLite only.

---

## 1. Guiding decisions (read first)

1. **SQLite stays the source of truth on-device.** The UI keeps writing to and
   reading from SQLite exactly as today. Sync is a background reconciler between
   local SQLite and cloud Postgres — never a data path the UI waits on.
2. **Cloud is additive, behind one boundary.** All cloud awareness lives behind
   a new `ICloudSyncService` / `ICloudAuth` boundary in
   `Data/Services/Cloud/`, mirroring how `IAnalyticsService` and
   `INotificationService` isolate their worlds. When cloud is off, a
   `NullCloudSyncService` is registered and no feature code changes behaviour.
3. **Sync-ready columns ship before any cloud code.** Phase 0 adds identity and
   change-tracking columns locally (additive — `sqlite-net` auto-adds columns,
   no migration). This is the only part that touches existing repositories, so
   it ships and soaks on its own, de-risking everything after it.
4. **No real-time sync initially.** Sync on launch, on foreground resume, after
   local writes (debounced), and manual refresh. The chronic-care cadence
   (a handful of events per day) doesn't justify realtime; it can be layered on
   later without changing the model.
5. **Simple conflict rules.** Append-only tables can't meaningfully conflict.
   One-row-per-day and profile tables use last-write-wins on a server-stamped
   `updated_at`. No CRDTs, no vector clocks, no merge UI.

## 2. Is Supabase the right backend? Yes.

| Need | Supabase fit |
|------|--------------|
| Relational data (pets → entries → logs, FKs, per-day uniqueness) | Native Postgres; the local schema maps 1:1. Firebase/Firestore would force denormalizing this. |
| Per-pet, per-caregiver access control | Row Level Security is exactly this; policies live next to the data. |
| Auth without building it | GoTrue: email OTP / magic link, Apple & Google sign-in. |
| Future documents/photos | Supabase Storage with the same RLS model. |
| Privacy posture | EU region hosting (matches the PostHog-EU stance), standard DPA, encryption at rest. |
| Exit strategy | Plain Postgres + open-source stack — dumpable, self-hostable. No proprietary data model. |
| Cost at MVP scale | Free tier → Pro $25/mo covers far past MVP volumes (sync payloads are tiny JSON rows). |

Alternatives considered: **Firebase** (NoSQL is a bad fit for this schema; no RLS
equivalent as clean; weaker EU/exit story), **PocketBase / self-hosted**
(operational burden for a solo project), **custom ASP.NET API** (most work, most
control — not justified until/unless Supabase limits actually bite, and the
`ICloudSyncService` boundary keeps that door open).

Client library: start with the community `supabase-csharp` package **wrapped
entirely behind the boundary** (no Supabase types leak past
`Data/Services/Cloud/`). If it proves heavy or flaky on MAUI, the fallback that
matches house style (hand-built PostHog capture) is direct `HttpClient` calls to
GoTrue + PostgREST — the API surface actually needed (auth, upsert, select
since-cursor, a few RPCs) is small.

## 3. What syncs and what never does

**Synced tables** (user data that must reach other devices/caregivers):

`Pet`, `PetEntry`, `Medication`, `MedicationSchedule`, `MedicationDoseLog`,
`Tracker`, `PetCondition`, `GlucoseEntry`, `AppetiteEntry`, `SeizureEntry`.

**Never synced** (device-local by nature):

- `ReminderInstance` — materialized OS-notification occurrences. Each device
  expands the *synced schedules* into its own instances via the existing
  idempotent `SyncMedicationAsync`. This falls out of the current architecture
  for free and is the reason reminders "just work" on every caregiver's device.
- `VetReportFile` + the `Reports/` folder — generated artifacts; regenerate per
  device. (Cloud copies become a Storage feature later, not sync.)
- `AppSettings` — device preferences (language).

**Removed before Phase 0:** `TrackingEntry` (legacy, no writers, no users to
preserve data for) is deleted entirely — model, service, DI, table creation,
reset line, the vet-report merge, and its doc mentions — in its own commit.

## 4. Local SQLite changes (Phase 0 groundwork)

Four additive columns on every synced entity, via a shared interface:

```csharp
public interface ISyncable
{
    string SyncId    { get; set; }  // GUID string, the global identity
    long   UpdatedAt { get; set; }  // UTC ticks, stamped on every write
    bool   IsDirty   { get; set; }  // true = not yet pushed
    bool   IsDeleted { get; set; }  // soft delete (synced tables stop hard-deleting)
}
```

- **`SyncId`** — a GUID generated locally at insert. Local `int Id` stays the
  primary key and every existing query keeps working; `SyncId` exists only for
  the cloud. A one-time idempotent backfill (run once from
  `AppDatabase.EnsureInitializedAsync`) assigns GUIDs to pre-existing rows.
- **`IsDirty` is the upload queue.** `WHERE IsDirty = 1` per table — no outbox
  table, no triggers, and immune to clock changes (unlike comparing
  `UpdatedAt` against a watermark).
- **`UpdatedAt`** exists for *conflict resolution* (LWW), not change detection.
- **`IsDeleted`**: repositories for synced tables switch `DeleteAsync` to
  soft-delete + dirty-stamp, and add `WHERE IsDeleted = 0` to reads. This is the
  one genuinely invasive Phase 0 change — every read path of the ten synced
  tables must filter it. (Hard-delete remains for never-synced tables, and
  `AppResetService` still hard-wipes everything.)

Stamping is centralized: repositories route saves through one small helper
(`SyncStamp.Touch(entity)`) so no call site can forget it. New table:

| Table | Purpose |
|-------|---------|
| `SyncState` | One row per synced table: `TableName`, `ServerCursor` (last server `updated_at` seen on pull). Plus rows for account state (user id, email) so Settings can render without a network call. |

`SyncState` (like every new table) is added to `AppDatabase.InitAsync` **and**
`AppResetService.ResetDataAsync` in the same commit, per the domain invariant.

**Local ↔ cloud FK mapping:** local rows reference `PetId`/`MedicationId` as
ints; the cloud uses UUIDs. The sync engine translates at the boundary using
in-memory maps built from `SyncId` lookups (pets and medications are small
tables). No FK columns change locally.

## 5. Cloud schema (Supabase / PostgreSQL)

All tables: `id uuid primary key` (the client-generated `SyncId`), `pet_id uuid`
FK where applicable, `updated_at timestamptz` **stamped by a server trigger**
(server time is the single conflict clock — client clocks are never trusted),
`deleted_at timestamptz null` for soft deletes.

```
auth.users                      (Supabase-managed)

profiles          id (= auth.uid), display_name, created_at
pets              id, name, type, age, created_at, updated_at, deleted_at
pet_members       pet_id, user_id, role ('owner'|'caregiver'), created_at
                  PK (pet_id, user_id)          ← the access-control hub
pet_invites       id, pet_id, code (unique), created_by, created_at,
                  expires_at, max_uses, use_count

pet_entries       id, pet_id, entry_date, mood, mood_level, mood_note,
                  include_in_vet_report, weight, mood_time, weight_time, …
                  UNIQUE (pet_id, entry_date)   ← preserves one-row-per-day
medications       id, pet_id, name, dosage, unit, notes, is_archived, created_at, …
medication_schedules  id, medication_id, day_of_week, time, …
medication_dose_logs  id, medication_id, pet_id, scheduled_date, scheduled_time,
                      status, resolved_at, …
                      UNIQUE (medication_id, scheduled_date, scheduled_time)
trackers          id, pet_id, tracker_id, kind, per_day_count, target_lo/hi,
                  unit, from_condition, …
pet_conditions    id, pet_id, condition_id, …    UNIQUE (pet_id, condition_id)
glucose_entries   id, pet_id, entry_date, entry_time, value, food_context, …
appetite_entries  id, pet_id, entry_date, entry_time, level, …
                  UNIQUE (pet_id, entry_date)
seizure_entries   id, pet_id, entry_date, entry_time, duration_minutes, note, …
```

**Ownership model (decided):** every pet has **exactly one owner — the user
who created it** — enforced by a partial unique index
(`UNIQUE (pet_id) WHERE role = 'owner'` on `pet_members`). Only the owner can
permanently delete the pet and its data. Everyone else is a `caregiver` with
shared read-write access who can leave (or be removed) at any time but can
never delete the pet.

**Relationships:** `auth.users 1—1 profiles`; users relate to pets **only**
through `pet_members` (ownership is the `role='owner'` row, so a future
ownership transfer stays a row update, not a schema change). All
data tables hang off `pets` exactly as they hang off `Pet` locally. The unique
constraints mirror the local domain invariants (one mood/weight/appetite row per
pet per day; one dose log per scheduled slot) so two caregivers' devices
converge instead of duplicating.

## 6. The sync engine

One new service, `CloudSyncService` (`Data/Services/Cloud/`), with a single
public entry point `SyncNowAsync()` — serialized behind a `SemaphoreSlim` gate
exactly like the reminder scheduler, so overlapping triggers can't interleave.

### Cycle: pull → apply → push

1. **Pull:** per table, `select * where updated_at > {ServerCursor} order by
   updated_at limit N` (paged). RLS automatically scopes this to the user's
   pets — the query is the same for backup and for shared pets.
2. **Apply:** for each remote row, look up local by `SyncId`:
   - not found → insert (mapping cloud FKs to local ints);
   - found and local row **clean** → overwrite local;
   - found and local row **dirty** → compare `updated_at`: remote newer →
     overwrite (local edit loses, LWW), else keep local (it will push next).
   - remote `deleted_at` set → soft-delete locally.
   Applied writes must **not** re-mark rows dirty (apply path bypasses
   `SyncStamp`). Advance `ServerCursor` per page, inside the same transaction
   as the applied rows.
3. **Push:** per table, read `WHERE IsDirty = 1`, translate FKs, and send
   batches to a small Postgres RPC `push_rows(table, jsonb)` that upserts by
   `id` **with a LWW guard** (`ON CONFLICT … DO UPDATE … WHERE excluded.client_updated_at
   >= t.client_updated_at`), then stamps server `updated_at`. On success, clear
   `IsDirty` on exactly the pushed rows (a write that happened mid-push stays
   dirty for the next cycle).
4. After any pull that changed medications/schedules, call the existing
   idempotent `SyncMedicationAsync` per affected medication so this device's
   `ReminderInstance` horizon re-materializes. This reuses the launch/boot
   catch-up machinery unchanged.
5. **Membership check:** each cycle starts by pulling the user's accessible
   pet list (`pet_members`). Any locally-synced pet **no longer in it** (the
   user left, was removed, or the owner deleted the pet) is **purged from the
   device** — RLS makes revoked pets invisible, so their tombstones can never
   arrive; this explicit diff is the only reliable signal. Purge = hard-delete
   the pet and all its dependent local rows + cancel its reminders.

### Why this shape

- **Pull-before-push** minimizes conflicts (you resolve against the freshest
  server state) and makes first-sync-on-new-device natural: empty cursors →
  full download.
- **Append-only tables** (glucose, seizure, dose logs, schedules-in-practice)
  make LWW almost never fire — the upsert is effectively insert-if-absent.
  Genuine conflicts are limited to two caregivers editing the *same* pet
  profile/medication/tracker or logging the same one-per-day metric — LWW with
  server time is the correct, comprehensible answer there ("the later entry
  replaced the earlier one", which is already the app's replace-on-relog rule).
- **Soft deletes** propagate as ordinary updates; nothing needs a tombstone
  protocol. A periodic server-side purge job (e.g. rows deleted > 90 days) can
  come much later.

### Triggers (when sync runs)

- App launch (off the UI path, alongside the existing reminder catch-up).
- App resume from background (throttled to at most once per few minutes).
- After a local dirty write, debounced ~30–60 s ("Caregiver B sees it soon,
  battery/network stays cheap").
- Manual pull-to-refresh affordance on the Journal.
- All fire-and-forget with a quiet failure mode: offline is normal, not an
  error. A tiny "last synced …" line in Settings → Cloud is the only status UI.

## 7. Security

- **Auth:** Supabase email + password with email verification, **plus Google
  Sign-In on Android** via the native account picker (Credential Manager →
  Google ID token → Supabase `signInWithIdToken`), since Android ships first
  and the one-tap picker is the smoothest onboarding. Password reset via email.
  Tokens stored in `SecureStorage`; auto-refresh inside the boundary.
  *Deferred consequence:* because a social login is offered, the iOS release
  will require **Sign in with Apple** (App Store policy) — planned for
  whenever iOS ships, not before.
- **RLS on every table, default deny.** The core policy, applied to all data
  tables:
  `pet_id IN (SELECT pet_id FROM pet_members WHERE user_id = auth.uid())`
  for select/insert/update. Owner-only policies for: deleting a pet, managing
  `pet_members`, creating invites. `pet_invites` is not readable by
  non-members; redemption goes through a `SECURITY DEFINER` RPC
  `redeem_invite(code)` that validates code/expiry/use-count and inserts the
  `pet_members` row — invite codes never grant read access by themselves.
- **Invite codes:** short-lived (e.g. 7 days), single-use by default,
  crypto-random in the `ABCD-1234` shape, rate-limited server-side (a simple
  attempts-per-user counter — no elaborate anti-abuse machinery).
- **Health-data posture:** pet health data is not GDPR special-category data,
  but Felova treats it as sensitive: EU region project, encryption at rest and
  in TLS (Supabase defaults), no third-party processors beyond Supabase.
- **"Delete all data" while signed in (decided):** one rule derived from
  ownership — the reset deletes every pet the user **owns** (cloud-wide),
  **removes them as caregiver** from every shared pet (never touching the
  pet itself), then wipes the device as today. No "device only vs cloud"
  question needed. Owned-pet deletion is a **soft-delete tombstone** (not an
  immediate hard delete) so the owner's *other* devices pull the deletion and
  converge to empty; a server-side purge hard-deletes tombstoned data later.
  **Account deletion** (its own Settings action, required by store policy) is
  the hard cascade: user row, owned pets, memberships, all dependent rows.
- **Analytics stays anonymous.** The PostHog `distinct_id` is never linked to
  the Supabase user id. New events stay coarse: `cloud_enabled`,
  `cloud_backup_completed`, `pet_share_invited`, `pet_share_joined` — no emails,
  codes, or ids.

## 8. UX flow (matching house patterns)

- **Settings → Cloud Features** opens the existing settings surface with a new
  section. Every input (sign-in form, invite entry, confirmations) rides the
  shared `FelovaBottomSheet` — no modal pages, per the app-wide rule.
- **Enable → account → migrate:** after first sign-in, if local pets exist:
  *"We found 2 pets on this device. Upload them to your cloud backup?"* Accept
  = mark all rows in all synced tables dirty and run a sync cycle (the normal
  push path **is** the migration — no special uploader). Decline = stay signed
  in, nothing uploads until they opt in (toggle remains in Settings).
- **Pet sharing** lives in the Manage-pet page: caregiver list, "Invite
  caregiver" (shows the code + share button), "Enter invite code" on the other
  device. After redeeming, the next sync cycle downloads the pet — no special
  "import" flow.
- **No mode switch anywhere else.** Journal, Today, reminders, vet report are
  untouched; remote entries simply appear on the timeline after a sync, exactly
  as if logged locally.
- Roles v1: `owner` and `caregiver` are both read-write over pet data
  (chronic-care logging is inherently symmetric); only owners manage sharing
  and can delete the pet. Finer permissions are deliberately deferred.
- **Leaving / being removed purges the device (decided):** a caregiver who
  leaves a pet — or whose access the owner revokes — has that pet's entire
  local copy removed on their device's next sync (see §6 step 5). No frozen
  offline copies of medical history for pets you no longer care for; the
  leave-pet sheet says so plainly before confirming.

## 9. Monetization fit

**Decision (owner):** there is no premium tier today. Cloud is part of the free
trial experience — the only gate is *creating an account*, and only for users
who opt into cloud. Nothing entitlement-shaped gets built now.

- **Free, no account (default):** full current app. Untouched, forever — this
  is the "private by default" differentiator, not a trial limitation.
- **What we build now:** zero billing code, zero trial timers, zero
  entitlement checks. "Enable Cloud Features" → create account → sync.
- **What we keep open for later:** a `profiles.plan` column (default `trial`)
  exists from day one so a future paid tier is a policy/RPC check, not a schema
  migration; per-account quotas (pets, members per pet, invite attempts) are
  single `count(*)` checks in the RPCs — that is the whole anti-abuse system,
  by design, and it's enough while everything is free.
- **Cost model:** text rows at chronic-care volume are near-free on the
  Supabase free tier; the practical cost driver later is Storage
  (documents/photos) — a decision for that phase.

## 10. Phased implementation plan

### Phase 0 — Sync-ready local schema (no cloud, no visible change)

- **Goal:** every synced table carries identity + change tracking; soft deletes
  in place; app behaves identically.
- **Features added:** none user-visible.
- **Technical changes:** `ISyncable` on the ten synced entities; `SyncStamp`
  helper; repositories stamp on write, soft-delete, filter `IsDeleted`;
  GUID backfill on init; `SyncState` table (+ reset wiring).
- **Database changes:** local only (additive columns auto-added by sqlite-net).
- **Backend changes:** none.
- **App changes:** none visible; full regression pass on delete/undo flows
  (undo restores rows — must un-soft-delete, not re-insert).
- **Complexity:** M (≈ 3–5 days). Mostly mechanical, but touches every repo.
- **Dependencies:** none.
- **Risks:** a missed `IsDeleted` filter shows "deleted" data (grep every read
  of the ten tables, including report `VetReportDataBuilder`); undo paths must
  keep their exact semantics. Ship this alone and soak it.

### Phase 1 — Accounts + cloud backup (single user, multi-device)

- **Goal:** a signed-in user's data is backed up and follows them to a second
  device.
- **Features added:** Settings → Cloud Features; sign up / sign in / sign out
  (email+password first, then the Google Sign-In slice); "upload existing
  pets" migration prompt; sync on launch/resume/debounce + manual refresh;
  "last synced" status; signed-in reset follows the decided ownership rule
  (§7); account deletion.
- **Technical changes:** `Data/Services/Cloud/` boundary (`ICloudAuth`,
  `ICloudSyncService`, `NullCloudSyncService`); sync engine (pull→apply→push,
  FK mapping, semaphore gate); `SecureStorage` token handling;
  post-pull `SyncMedicationAsync` re-arm.
- **Database changes:** cloud schema §5 minus sharing tables; local `SyncState`
  gains account rows.
- **Backend changes:** Supabase project (EU), tables + `updated_at` triggers,
  RLS (single-user policies via `pet_members` from day one — the sharing model
  with only owner rows), `push_rows` RPC.
- **App changes:** new settings section + auth sheets; localization EN/DE for
  all new strings; new analytics events.
- **Complexity:** L (≈ 2–3 weeks). The sync engine's apply/push correctness is
  the bulk.
- **Dependencies:** Phase 0 soaked; Supabase project (EU) + URL/anon key;
  `supabase-csharp` evaluated on Android early (spike first — it is the main
  unknown). The Google Sign-In slice additionally needs a Google Cloud OAuth
  client (web + Android client IDs, the app's signing SHA-1) wired into the
  Supabase Google provider — owner runs the console steps from written
  instructions; email+password auth does not wait for it.
- **Risks:** first-sync volume (page the pulls; migrate per-table); clock
  discipline (server stamps only); silent auth expiry (surface as "signed out"
  in Settings, never as a crash); double-writes if the apply path accidentally
  stamps dirty (unit-test the engine against an in-memory SQLite — the engine
  must be a plain class with no MAUI dependency, like the report document
  layer, precisely so it's testable).

### Phase 2 — Multi-caregiver sharing

- **Goal:** two accounts log for the same pet and converge automatically.
- **Features added:** caregiver list + invite code generation in Manage-pet;
  "enter invite code" join flow; leave/remove caregiver; shared pets appear in
  the joiner's pet list.
- **Technical changes:** invite sheets; membership awareness in the pet list
  (badge "shared" — no other UI change); conflict handling already exists (LWW
  from Phase 1).
- **Database changes:** none locally.
- **Backend changes:** `pet_invites` table, `redeem_invite` RPC, owner-only
  policies, rate limiting, member-management RPCs.
- **App changes:** Manage-pet section, join sheet, EN/DE strings, share-sheet
  for the code.
- **Complexity:** M (≈ 1–1.5 weeks) — the sync engine already does the hard
  part; this phase is mostly backend policy + UI.
- **Dependencies:** Phase 1 in production.
- **Risks:** cross-account edge cases (owner deletes pet while caregiver
  offline → caregiver's push must fail gracefully to "this pet is no longer
  shared"); reminder duplication across caregivers is *correct* (each device
  reminds its user) but must be explained in the sharing UI copy; dose logged
  by A doesn't cancel B's already-fired reminder — accepted, documented.

### Phase 3 — Polish & scale (as needed, in value order)

- **Goal:** round out cloud into a durable premium tier.
- **Candidates, each independent:** premium gating wired to store billing;
  periodic background sync (platform background tasks — Android WorkManager
  first); Supabase Storage for vet-report PDFs / future photos (same
  `pet_members` RLS); shareable invite *links* (deep links); optional Supabase
  Realtime for open-app freshness; server-side purge of old soft-deletes;
  ownership transfer.
- **Complexity:** S–M per item.
- **Dependencies:** Phases 1–2 stable; real usage data to pick the order.
- **Risks:** background execution limits on mobile (the launch/resume/debounce
  triggers already cover the product need — background sync is an optimization,
  don't let it block anything).

---

## Resolved questions (owner, 2026-07-22)

1. **Premium timing:** no premium exists. Cloud ships free (trial includes it);
   the only gate is account creation for cloud opt-in. Schema keeps
   `profiles.plan` so gating later is cheap.
2. **Legacy `TrackingEntry`:** removed entirely before Phase 0 — no users, no
   history worth preserving.
3. **Ownership:** exactly one owner per pet (its creator); only the owner can
   delete the pet; caregivers share access and can leave, never delete. (§5)
4. **Reset semantics when signed in:** derived from ownership — owned pets are
   permanently deleted, caregiver memberships are removed, device is wiped. (§7)
5. **Auth:** email+password with verification **+ Google Sign-In on Android**
   (native account picker). Sign in with Apple becomes required when iOS ships. (§7)
6. **Caregiver leaving/removal:** the pet's data is purged from their device. (§6/§8)
7. **Working rhythm:** feature branch; implement in verified slices
   (`TrackingEntry` removal → Phase 0 → Phase 1 → Phase 2) with an owner
   test-pause between slices.

*No open questions — implementation can start.*

## Project config (safe-to-embed, like the PostHog project key)

- Supabase project URL: `https://pbwhusssrzavdgbvjtrv.supabase.co`
- Publishable key: `sb_publishable_vAPqc7ARaJiI_39xTDUftA_zXWRIuXc`
  (new-style key replacing the legacy `anon` JWT; the service-role key is
  never stored in this repo)
