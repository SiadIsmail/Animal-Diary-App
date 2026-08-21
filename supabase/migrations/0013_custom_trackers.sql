-- ═══════════════════════════════════════════════════════════════════════════
--  0013: trackers the owner defines themselves.
--
--  The app ships six trackers, each of which earns a bespoke input sheet, a
--  condition seed, or its own shape in the vet report. Everything else an owner
--  wants to write down (a walk, a groom, a poop) is the same thing wearing a
--  different name, so it becomes DATA instead of another pair of tables:
--
--    • custom_trackers: the owner's definition (name, icon, colour token, shape,
--      unit, cadence). The cadence lives HERE, not in a sibling `trackers` row:
--      that table converges on (pet_id, tracker_id), so every custom tracker would
--      collapse onto one row the moment a second device pulled.
--    • custom_entries : occurrences, ADDITIVE like glucose_entries: many per day,
--      keyed by id. One conflict key serves every custom tracker there will ever
--      be, which is what makes "as many as you like" free on this side.
--
--  Both mirror the 0007 / 0008 setup (table shape, updated_at trigger + pull-cursor
--  index, member-scoped RLS, and the GRANT that 0009 exists to remind us about,
--  RLS gates rows, the GRANT gates whether the role may touch the table at all, and
--  omitting it fails the pull with 42501 and aborts the whole sync).
--
--  custom_entries cascades from pets (NOT only from custom_trackers), so hard
--  account deletion reaches it through public.pets exactly like every other data
--  table: the omission migration 0012 had to fix.
-- ═══════════════════════════════════════════════════════════════════════════

-- The owner's definition. `name` is user text: never interpreted, never matched on.
create table public.custom_trackers (
  id                uuid primary key,
  pet_id            uuid not null references public.pets (id) on delete cascade,
  name              text not null default '',
  icon              text not null default '',
  color_key         text not null default '',
  shape             text not null,
  unit              text not null default '',
  kind              text not null,
  per_day_count     int  not null default 0,
  is_archived       boolean not null default false,
  client_updated_at timestamptz not null,
  deleted_at        timestamptz,
  created_at        timestamptz not null default now(),
  updated_at        timestamptz not null default now()
);

-- Occurrences, additive events (mirrors glucose_entries; keyed by id).
-- pet_id is carried alongside custom_tracker_id so RLS, the membership purge and
-- the account cascade all reach a row in one hop, the same way dose logs do.
create table public.custom_entries (
  id                uuid primary key,
  pet_id            uuid not null references public.pets (id) on delete cascade,
  custom_tracker_id uuid not null references public.custom_trackers (id) on delete cascade,
  entry_date        date not null,
  time_ticks        bigint not null,
  amount            numeric,
  note              text not null default '',
  client_updated_at timestamptz not null,
  deleted_at        timestamptz,
  created_at        timestamptz not null default now(),
  updated_at        timestamptz not null default now()
);

create index custom_entries_tracker_idx on public.custom_entries (custom_tracker_id);

-- Server time is the ONLY pull cursor; clients never write updated_at. Member-
-- scoped RLS through the pet (identical to every other data table). One loop for
-- both new tables, as 0007 did.
do $$
declare t text;
begin
  foreach t in array array['custom_trackers', 'custom_entries']
  loop
    execute format(
      'create trigger stamp_updated_at before insert or update on public.%I
         for each row execute function public.stamp_updated_at()', t);
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
    -- table at all. Both required: see 0002 / 0009.
    execute format('grant select, insert, update on public.%I to authenticated', t);
  end loop;
end $$;

-- ── push_rows: add the two custom tables' natural keys ──────────────────────
-- Both converge on id: the definition because two devices' "Walk" are genuinely
-- two different trackers until one syncs (identity is the SyncId, as for
-- medications), and the entries because they are additive events. Everything else
-- is byte-for-byte 0008 (the current definition): only conflict_cols gains two
-- branches.
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
    when 'glucose_entries'         then 'id'
    when 'seizure_entries'         then 'id'
    when 'water_amount_entries'    then 'id'
    when 'appetite_amount_entries' then 'id'
    when 'custom_trackers'         then 'id'
    when 'custom_entries'          then 'id'
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

  -- Authorization: explicit because this function bypasses table RLS. Both new
  -- tables carry pet_id, so the generic branch below covers them.
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
