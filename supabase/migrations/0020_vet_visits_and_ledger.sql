-- ═══════════════════════════════════════════════════════════════════════════
--  0020 — the appointment layer: a treatment ledger, vet visits, and the
--         questions an owner means to ask at one.
--
--  Three tables, one migration, because they are one feature and migrations are
--  append-only once applied — splitting them would mean editing this file later,
--  which is the one thing a numbered migration may never do. The client ships them
--  in phases; a table that exists ahead of the code that writes to it is inert.
--
--    • medication_changes — what a medication USED TO BE. Until it existed, an edit
--      overwrote `dosage` in place and destroyed the answer to the one question a vet
--      asks about a treatment ("what was he on in March?"). It cannot be backfilled,
--      so every day without it lost history on every device.
--    • vet_visits         — the appointment as a thing the app knows about.
--    • vet_questions      — a running list between visits, in the owner's own words.
--
--  All three mirror the 0007 / 0008 / 0013 setup: updated_at trigger + pull-cursor
--  index, member-scoped RLS through the pet, and the GRANT that 0009 exists to
--  remind us about — RLS gates which ROWS, the GRANT gates whether the role may
--  touch the table at all, and omitting it fails the pull with 42501 and aborts the
--  caller's entire sync, every table included.
--
--  Every one of them cascades from public.pets, so hard account deletion reaches
--  them through the two deletes in delete_my_account() — the omission migration 0012
--  had to fix.
--
--  Column-naming note: `created_at` and `updated_at` are SERVER-owned and excluded
--  from push_rows' column list, so a client-owned timestamp can never take those
--  names. Hence `changed_at`, `asked_at`, `answered_at` (and `med_created_at` before
--  them) — the client's clock, under a name of its own.
-- ═══════════════════════════════════════════════════════════════════════════

-- ── 1. The treatment ledger ────────────────────────────────────────────────
--
-- medication_id is a PLAIN uuid with NO foreign key, and that is the design.
-- A reference cascading from medications would delete the history of the very
-- thing whose history this table exists to preserve; even `on delete set null`
-- would order this table's pushes behind the medications table's for a pointer
-- nothing reads. The row is self-contained instead: medication_name and summary
-- are rendered at the moment of the change and stored as text, so it stays
-- readable after a rename, an archive, or a delete — the same reasoning behind a
-- custom tracker's stored unit, and behind the vet report counting scheduled
-- doses from dose logs rather than from current schedule rows.
--
-- `kind` is text with no CHECK constraint, for the reason 0017 spells out: a
-- constraint here means a newer client's eighth kind raises a violation that
-- aborts that caller's whole sync against a project this migration has reached
-- but a later one has not. The client parses defensively instead. The member
-- names in Data/Models/MedicationChange.cs are the wire format
-- ('Started', 'DoseChanged', 'ScheduleChanged', 'Renamed', 'Archived',
-- 'Restored', 'Stopped'); renaming one there orphans every row here.
create table public.medication_changes (
  id                uuid primary key,
  pet_id            uuid not null references public.pets (id) on delete cascade,
  medication_id     uuid,
  changed_at        timestamptz not null,
  kind              text not null,
  medication_name   text not null default '',
  summary           text not null default '',
  note              text not null default '',
  client_updated_at timestamptz not null,
  deleted_at        timestamptz,
  created_at        timestamptz not null default now(),
  updated_at        timestamptz not null default now()
);

comment on column public.medication_changes.medication_id is
  'A pointer, never a dependency: no FK, nullable, and unresolvable on a device '
  'that never held the medication. Read medication_name and summary instead.';

-- ── 2. Vet visits ──────────────────────────────────────────────────────────
--
-- No `status` column: a visit is past if its date is in the past, derived the way
-- Pet.AgeYears is. No practice or vet entity either — these are free-text labels
-- for context, exactly like appetite_entries.food.
--
-- time_ticks is NULLABLE and has no default. Owners often know the day and not
-- the slot, and this app never fabricates an unknown part of a date — the same
-- rule that leaves pets.birth_month null rather than inventing January.
create table public.vet_visits (
  id                uuid primary key,
  pet_id            uuid not null references public.pets (id) on delete cascade,
  visit_date        date not null,
  time_ticks        bigint,
  practice          text not null default '',
  vet_name          text not null default '',
  visit_note        text not null default '',
  client_updated_at timestamptz not null,
  deleted_at        timestamptz,
  created_at        timestamptz not null default now(),
  updated_at        timestamptz not null default now()
);

comment on column public.vet_visits.time_ticks is
  'Time of day as .NET TimeSpan ticks, or NULL when the owner knows the day but '
  'not the time. NULL is an answer, never a placeholder to fill in.';

-- ── 3. Questions for the vet ───────────────────────────────────────────────
--
-- No link to a visit. A question is open or answered; that is the whole state
-- machine, and tying it to a visit would need a cross-table reference for one bit
-- of information nobody has asked for. question_text is the owner's words, verbatim
-- — named that way rather than `text` because a bare column called `text` is a type
-- name everywhere else and push_rows builds its column list as SQL text.
create table public.vet_questions (
  id                uuid primary key,
  pet_id            uuid not null references public.pets (id) on delete cascade,
  question_text     text not null default '',
  asked_at          timestamptz not null,
  answered_at       timestamptz,
  client_updated_at timestamptz not null,
  deleted_at        timestamptz,
  created_at        timestamptz not null default now(),
  updated_at        timestamptz not null default now()
);

comment on column public.vet_questions.answered_at is
  'NULL = still open. The entire state machine.';

-- ── Trigger, pull-cursor index, RLS and GRANT — one loop, as 0007/0013 did ──
do $do$
declare t text;
begin
  foreach t in array array['medication_changes', 'vet_visits', 'vet_questions']
  loop
    execute format(
      'create trigger stamp_updated_at before insert or update on public.%I
         for each row execute function public.stamp_updated_at()', t);
    -- Server time is the ONLY pull cursor; clients never write updated_at.
    execute format(
      'create index %I on public.%I (updated_at)', t || '_updated_at_idx', t);

    execute format('alter table public.%I enable row level security', t);
    execute format(
      'create policy "members read"   on public.%I for select using (public.is_pet_member(pet_id))', t);
    execute format(
      'create policy "members insert"  on public.%I for insert with check (public.is_pet_member(pet_id))', t);
    execute format(
      'create policy "members update"  on public.%I for update using (public.is_pet_member(pet_id))', t);

    -- RLS decides which ROWS; the GRANT decides whether the role may touch the
    -- table at all. Both required — see 0002 / 0009.
    execute format('grant select, insert, update on public.%I to authenticated', t);
  end loop;
end $do$;

-- ── push_rows: add the three tables' natural keys ──────────────────────────
-- All three converge on id. Every row is a distinct moment — a change that
-- happened, a visit that was booked, a question that was written down — appended
-- and never merged with a sibling. Byte-for-byte 0013 (the current definition)
-- apart from the three new conflict_cols branches; all three carry pet_id, so the
-- generic authorization branch below already covers them.
create or replace function public.push_rows(p_table text, p_rows jsonb)
returns void
language plpgsql
security definer set search_path = ''
as $$
declare
  uid uuid;
  conflict_cols text;
  col_list text;
  set_list text;
  bad boolean;
begin
  uid := (select auth.uid());
  if uid is null then
    raise exception 'push_rows: no authenticated user in request context';
  end if;

  conflict_cols := case p_table
    when 'pets'                    then 'id'
    when 'medications'             then 'id'
    when 'medication_schedules'    then 'id'
    when 'medication_changes'      then 'id'
    when 'glucose_entries'         then 'id'
    when 'seizure_entries'         then 'id'
    when 'water_amount_entries'    then 'id'
    when 'appetite_amount_entries' then 'id'
    when 'custom_trackers'         then 'id'
    when 'custom_entries'          then 'id'
    when 'vet_visits'              then 'id'
    when 'vet_questions'           then 'id'
    when 'pet_entries'             then 'pet_id, entry_date'
    when 'appetite_entries'        then 'pet_id, entry_date'
    when 'water_level_entries'     then 'pet_id, entry_date'
    when 'medication_dose_logs'    then 'medication_id, scheduled_date, scheduled_time_ticks'
    when 'pet_conditions'          then 'pet_id, condition_id'
    when 'trackers'                then 'pet_id, tracker_id'
    else null
  end;
  if conflict_cols is null then
    raise exception 'push_rows: table % is not syncable', p_table;
  end if;

  -- Authorization — explicit because this function bypasses table RLS. All three
  -- new tables carry pet_id, so the generic branch below covers them.
  if p_table = 'pets' then
    execute
      'select exists (
         select 1 from jsonb_populate_recordset(null::public.pets, $1) r
         join public.pets t on t.id = r.id
         where not public.is_pet_member(t.id))'
      into bad using p_rows;
  elsif p_table = 'medication_schedules' then
    execute
      'select exists (
         select 1 from jsonb_populate_recordset(null::public.medication_schedules, $1) r
         left join public.medications m on m.id = r.medication_id
         where m.id is null or not public.is_pet_member(m.pet_id))'
      into bad using p_rows;
  else
    execute format(
      'select exists (
         select 1 from jsonb_populate_recordset(null::public.%I, $1) r
         where not public.is_pet_member(r.pet_id))', p_table)
      into bad using p_rows;
  end if;
  if bad then
    raise exception 'push_rows: a row targets a pet the caller is not a member of';
  end if;

  -- The client owns every column except the two server-stamped ones. One
  -- ordered list, used for both the target columns and the select projection,
  -- so positions always line up.
  select string_agg(quote_ident(column_name), ', ' order by ordinal_position)
    into col_list
    from information_schema.columns
   where table_schema = 'public' and table_name = p_table
     and column_name not in ('created_at', 'updated_at');

  select string_agg(format('%I = excluded.%I', column_name, column_name), ', ')
    into set_list
    from information_schema.columns
   where table_schema = 'public' and table_name = p_table
     and column_name not in ('id', 'created_at', 'updated_at');

  execute format(
    'insert into public.%I as t (%s)
       select %s from jsonb_populate_recordset(null::public.%I, $1)
     on conflict (%s) do update set %s
       where excluded.client_updated_at >= t.client_updated_at',
    p_table, col_list, col_list, p_table, conflict_cols, set_list)
  using p_rows;
end $$;

revoke execute on function public.push_rows(text, jsonb) from anon;
