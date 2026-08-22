-- ═══════════════════════════════════════════════════════════════════════════
--  0023: a seizure's duration moves from whole minutes to seconds.
--
--  duration_minutes is an integer, so a 45-second seizure was unrecordable: it
--  landed as 0 or 1. Sub-minute events are common and clinically relevant, and
--  owners describe seizures in seconds ("about forty seconds"), never in fractions
--  of a minute. That is data loss rather than a usability wrinkle, so the canonical
--  unit changes rather than a label being hung over a number that has already lost
--  the information.
--
--  THE OLD COLUMN STAYS. Not laziness: a household device still running an older
--  build pushes duration_minutes and nothing else, and dropping the column would
--  make that row's duration vanish server-side rather than arrive and be converted.
--  The client keeps the property for the same reason and never reads it
--  (Data/Models/JournalEntries.cs, AI/known-constraints.md).
--
--  The backfill is idempotent by its WHERE clause rather than by a run-once flag,
--  which is the stronger guard: a second pass matches nothing, and a legacy row
--  pushed next month is converted when it arrives instead of being missed by a
--  migration that already ran. The client runs the identical statement locally on
--  every launch, for exactly that reason.
--
--  push_rows needs no change: it derives its column list from
--  information_schema.columns (see 0013/0014/0017), so the new column is picked up
--  on the next call.
--
--  Ordering note, the same trade every additive column here has taken: shipping the
--  client before this is applied loses the seconds value until the column exists
--  (jsonb_populate_recordset drops unknown keys), and an OLD client pushing an
--  update to a row nulls the new column, because the upsert's SET list writes every
--  column from the incoming record. Seizure entries are only ever inserted or
--  soft-deleted, never edited, so that window is a delete from a stale device.
-- ═══════════════════════════════════════════════════════════════════════════

alter table public.seizure_entries
  add column if not exists duration_seconds integer;

update public.seizure_entries
   set duration_seconds = duration_minutes * 60
 where duration_seconds is null
   and duration_minutes is not null;

comment on column public.seizure_entries.duration_seconds is
  'How long the seizure lasted, in SECONDS. NULL means the owner did not time it, '
  'which is a normal answer and never a guess. Canonical: the unit the owner typed '
  'it in is recorded separately in seizure_entries.unit.';

comment on column public.seizure_entries.duration_minutes is
  'DEAD COLUMN, superseded by duration_seconds in 0023. Kept only so a client on an '
  'older build can still deliver a duration, which the backfill converts. Never read '
  'by current clients; do not write it.';
