-- ═══════════════════════════════════════════════════════════════════════════
--  0008 — appetite: exact grams + food context.
--
--  Appetite gains the same two-mode shape as water (0007):
--    • appetite_entries keeps the qualitative one-per-day reading and gains an
--      optional `food` label.
--    • appetite_amount_entries is NEW — exact grams, ADDITIVE like glucose (keyed
--      by id, summed per day by the report), also with an optional `food` label.
--
--  Everything mirrors the water setup; push_rows gains one branch
--  (appetite_amount_entries → id). Food is free text, never a food entity.
-- ═══════════════════════════════════════════════════════════════════════════

-- Optional free-text food context on the qualitative reading. Additive column;
-- default '' matches the client's non-null string.
alter table public.appetite_entries add column food text not null default '';

-- Exact grams, additive events (mirrors water_amount_entries; keyed by id).
create table public.appetite_amount_entries (
  id                uuid primary key,
  pet_id            uuid not null references public.pets (id) on delete cascade,
  entry_date        date not null,
  time_ticks        bigint not null,
  grams             numeric not null,
  food              text not null default '',
  client_updated_at timestamptz not null,
  deleted_at        timestamptz,
  created_at        timestamptz not null default now(),
  updated_at        timestamptz not null default now()
);

-- Server time is the ONLY pull cursor; clients never write updated_at. Member-
-- scoped RLS through the pet (identical to the other data tables).
create trigger stamp_updated_at before insert or update on public.appetite_amount_entries
  for each row execute function public.stamp_updated_at();
create index appetite_amount_entries_updated_at_idx on public.appetite_amount_entries (updated_at);

alter table public.appetite_amount_entries enable row level security;
create policy "members read"   on public.appetite_amount_entries for select using (public.is_pet_member(pet_id));
create policy "members insert"  on public.appetite_amount_entries for insert with check (public.is_pet_member(pet_id));
create policy "members update"  on public.appetite_amount_entries for update using (public.is_pet_member(pet_id));

-- ── push_rows: add appetite_amount_entries' natural key ─────────────────────
-- Additive events converge on id. Everything else is byte-for-byte 0007 (the
-- current definition) — only conflict_cols gains one branch.
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
