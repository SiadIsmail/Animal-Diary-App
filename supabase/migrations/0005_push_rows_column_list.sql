-- ═══════════════════════════════════════════════════════════════════════════
--  0005 — push_rows: insert an explicit column list.
--
--  `insert … select * from jsonb_populate_recordset(...)` writes EXPLICIT
--  NULLs for columns the client never sends (created_at, updated_at), and an
--  explicit NULL overrides a column default → 23502 on created_at. Inserting
--  through a column list that omits the two server-owned columns lets their
--  defaults (and the updated_at trigger) do their job. Everything else is
--  identical to 0004.
-- ═══════════════════════════════════════════════════════════════════════════

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
    when 'pet_entries'          then 'pet_id, entry_date'
    when 'appetite_entries'     then 'pet_id, entry_date'
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
