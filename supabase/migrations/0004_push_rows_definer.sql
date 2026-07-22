-- ═══════════════════════════════════════════════════════════════════════════
--  0004 — make push_rows SECURITY DEFINER with explicit authorization.
--
--  Even with the pets INSERT policy at `to authenticated with check (true)`
--  (0003), the upsert inside push_rows still failed RLS on an EMPTY table —
--  enforcement inside the RPC is not matching the policy model, so the push
--  path stops relying on it. The function now runs as its owner (bypassing
--  table RLS) and performs the SAME rule itself, loudly:
--    - caller must be authenticated (named error if the auth context is gone);
--    - every row must target a pet the caller is a MEMBER of;
--    - brand-new pets are allowed — the ownership trigger makes them the
--      caller's (and raises its own named error without auth context).
--  The pull path (PostgREST GET) still goes through RLS unchanged.
-- ═══════════════════════════════════════════════════════════════════════════

-- Tiny diagnostic: what the request context actually looks like from inside.
create or replace function public.whoami()
returns jsonb
language sql stable
set search_path = ''
as $$
  select jsonb_build_object(
    'db_role', current_user::text,
    'uid', (select auth.uid())::text);
$$;

grant execute on function public.whoami() to authenticated;

create or replace function public.push_rows(p_table text, p_rows jsonb)
returns void
language plpgsql
security definer set search_path = ''
as $$
declare
  uid uuid;
  conflict_cols text;
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
    -- Updating an EXISTING pet requires membership; unknown ids are new pets
    -- and become the caller's via the ownership trigger on insert.
    execute
      'select exists (
         select 1 from jsonb_populate_recordset(null::public.pets, $1) r
         join public.pets t on t.id = r.id
         where not public.is_pet_member(t.id))'
      into bad using p_rows;
  elsif p_table = 'medication_schedules' then
    -- No pet_id column — authorize through the parent medication.
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

  select string_agg(format('%I = excluded.%I', column_name, column_name), ', ')
    into set_list
    from information_schema.columns
   where table_schema = 'public' and table_name = p_table
     and column_name not in ('id', 'created_at', 'updated_at');

  execute format(
    'insert into public.%I as t
       select * from jsonb_populate_recordset(null::public.%I, $1)
     on conflict (%s) do update set %s
       where excluded.client_updated_at >= t.client_updated_at',
    p_table, p_table, conflict_cols, set_list)
  using p_rows;
end $$;
