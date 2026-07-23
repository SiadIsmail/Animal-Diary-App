-- ═══════════════════════════════════════════════════════════════════════════
--  0007 — water intake: two stores, one per mode.
--
--  Water has two independent shapes (and a table can carry only one push_rows
--  conflict key), so it is two tables — the same event-vs-one-per-day split the
--  rest of the schema already uses:
--
--    • water_amount_entries — exact millilitre readings, ADDITIVE like
--      glucose_entries: many per day, keyed by id, summed per day by the report.
--    • water_level_entries  — the relative reading, ONE per day like
--      appetite_entries: unique (pet_id, entry_date).
--
--  Both mirror the 0001 setup for their sibling (table shape, updated_at trigger
--  + pull-cursor index, member-scoped RLS) and extend push_rows' natural-key map
--  — the only change to the write RPC from 0005.
-- ═══════════════════════════════════════════════════════════════════════════

-- Exact ml, additive events (mirrors glucose_entries; keyed by id).
create table public.water_amount_entries (
  id                uuid primary key,
  pet_id            uuid not null references public.pets (id) on delete cascade,
  entry_date        date not null,
  time_ticks        bigint not null,
  amount_ml         numeric not null,
  client_updated_at timestamptz not null,
  deleted_at        timestamptz,
  created_at        timestamptz not null default now(),
  updated_at        timestamptz not null default now()
);

-- Relative level, one per day (mirrors appetite_entries; unique on the day).
create table public.water_level_entries (
  id                uuid primary key,
  pet_id            uuid not null references public.pets (id) on delete cascade,
  entry_date        date not null,
  time_ticks        bigint not null,
  level             int not null,
  client_updated_at timestamptz not null,
  deleted_at        timestamptz,
  created_at        timestamptz not null default now(),
  updated_at        timestamptz not null default now(),
  unique (pet_id, entry_date)
);

-- Server time is the ONLY pull cursor; clients never write updated_at. Member-
-- scoped RLS through the pet (identical to the other data tables). One loop for
-- both new tables.
do $$
declare t text;
begin
  foreach t in array array['water_amount_entries', 'water_level_entries']
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
  end loop;
end $$;

-- ── push_rows: add the two water tables' natural keys ───────────────────────
-- Amounts converge on id (additive events); levels on (pet, day). Everything
-- else is byte-for-byte 0005 (the current definition) — only conflict_cols gains
-- two branches.
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
    when 'pets'                 then 'id'
    when 'medications'          then 'id'
    when 'medication_schedules' then 'id'
    when 'glucose_entries'      then 'id'
    when 'seizure_entries'      then 'id'
    when 'water_amount_entries' then 'id'
    when 'pet_entries'          then 'pet_id, entry_date'
    when 'appetite_entries'     then 'pet_id, entry_date'
    when 'water_level_entries'  then 'pet_id, entry_date'
    when 'medication_dose_logs' then 'medication_id, scheduled_date, scheduled_time_ticks'
    when 'pet_conditions'       then 'pet_id, condition_id'
    when 'trackers'             then 'pet_id, tracker_id'
    else null
  end;
  if conflict_cols is null then
    raise exception 'push_rows: table % is not syncable', p_table;
  end if;

  -- Authorization — explicit because this function bypasses table RLS.
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
