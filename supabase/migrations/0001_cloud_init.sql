-- ═══════════════════════════════════════════════════════════════════════════
--  Felova cloud schema — Phase 1 (accounts + backup + multi-device sync).
--  Run this in the Supabase SQL editor (or `supabase db push`). Idempotent-ish:
--  written to be run ONCE on a fresh project; see supabase/README.md.
--
--  Design (see CLOUD_SYNC_PLAN.md in the repo root):
--  - Row ids are client-generated GUIDs (the local SyncId).
--  - `updated_at` is SERVER-stamped by trigger and is only the pull cursor.
--  - `client_updated_at` is the writer's clock and is the last-write-wins
--    comparator (client clocks are only ever compared with client clocks).
--  - Deletes are soft (`deleted_at`) so they propagate to other devices.
--  - Access control hangs entirely off pet_members (owner|caregiver).
-- ═══════════════════════════════════════════════════════════════════════════

-- ── profiles ────────────────────────────────────────────────────────────────
-- One row per auth user, created automatically on signup. `plan` exists so a
-- future paid tier is a policy check, not a schema migration (today: 'trial').

create table public.profiles (
  id         uuid primary key references auth.users (id) on delete cascade,
  plan       text not null default 'trial',
  created_at timestamptz not null default now()
);

alter table public.profiles enable row level security;

create policy "profiles: own row" on public.profiles
  for select using (id = (select auth.uid()));

create function public.handle_new_user()
returns trigger
language plpgsql
security definer set search_path = ''
as $$
begin
  insert into public.profiles (id) values (new.id);
  return new;
end $$;

create trigger on_auth_user_created
  after insert on auth.users
  for each row execute function public.handle_new_user();

-- ── pets + membership ───────────────────────────────────────────────────────

create table public.pets (
  id                uuid primary key,
  name              text not null default '',
  type              text not null default '',
  age               int  not null default 0,          -- legacy snapshot column
  birth_year        int  not null default 0,
  birth_month       int,
  birth_day         int,
  condition_id      text not null default '',          -- legacy single condition
  client_updated_at timestamptz not null,
  deleted_at        timestamptz,
  created_at        timestamptz not null default now(),
  updated_at        timestamptz not null default now()
);

create table public.pet_members (
  pet_id     uuid not null references public.pets (id) on delete cascade,
  user_id    uuid not null references auth.users (id) on delete cascade,
  role       text not null check (role in ('owner', 'caregiver')),
  created_at timestamptz not null default now(),
  primary key (pet_id, user_id)
);

-- Exactly one owner per pet — the ownership model is structural.
create unique index pet_members_one_owner on public.pet_members (pet_id)
  where role = 'owner';

-- The creator becomes the owner automatically and atomically.
create function public.handle_new_pet()
returns trigger
language plpgsql
security definer set search_path = ''
as $$
begin
  insert into public.pet_members (pet_id, user_id, role)
  values (new.id, (select auth.uid()), 'owner');
  return new;
end $$;

create trigger on_pet_created
  after insert on public.pets
  for each row execute function public.handle_new_pet();

-- Membership check used by every data-table policy. SECURITY DEFINER so the
-- lookup itself bypasses RLS on pet_members (avoids recursive policies).
create function public.is_pet_member(p_pet uuid)
returns boolean
language sql stable
security definer set search_path = ''
as $$
  select exists (
    select 1 from public.pet_members
    where pet_id = p_pet and user_id = (select auth.uid())
  );
$$;

alter table public.pets enable row level security;

create policy "pets: members read"   on public.pets for select using (public.is_pet_member(id));
create policy "pets: members write"  on public.pets for update using (public.is_pet_member(id));
create policy "pets: signed-in insert" on public.pets for insert with check ((select auth.uid()) is not null);
-- No delete policy: the app soft-deletes; hard deletion happens only via the
-- delete_* RPCs below / the auth-user cascade.

alter table public.pet_members enable row level security;

create policy "pet_members: own rows" on public.pet_members
  for select using (user_id = (select auth.uid()));

-- ── data tables ─────────────────────────────────────────────────────────────
-- Column names mirror the local SQLite entities (snake_case). Times of day are
-- stored as their .NET TimeSpan tick counts (bigint) and date-only values as
-- `date` — no timezone reinterpretation on either side.

create table public.pet_entries (
  id                    uuid primary key,
  pet_id                uuid not null references public.pets (id) on delete cascade,
  entry_date            date not null,
  mood                  text not null default '',
  mood_level            int  not null default 0,
  mood_note             text not null default '',
  include_in_vet_report boolean not null default false,
  weight                numeric not null default 0,
  mood_time_ticks       bigint,
  weight_time_ticks     bigint,
  client_updated_at     timestamptz not null,
  deleted_at            timestamptz,
  created_at            timestamptz not null default now(),
  updated_at            timestamptz not null default now(),
  unique (pet_id, entry_date)          -- one row per pet per day, cloud-enforced
);

create table public.medications (
  id                uuid primary key,
  pet_id            uuid not null references public.pets (id) on delete cascade,
  name              text not null default '',
  dosage            numeric not null default 0,
  unit              text not null default '',
  notes             text not null default '',
  is_archived       boolean not null default false,
  med_created_at    timestamptz,       -- Medication.CreatedAt (bounds dose expansion)
  client_updated_at timestamptz not null,
  deleted_at        timestamptz,
  created_at        timestamptz not null default now(),
  updated_at        timestamptz not null default now()
);

create table public.medication_schedules (
  id                uuid primary key,
  medication_id     uuid not null references public.medications (id) on delete cascade,
  day_of_week       int not null,      -- .NET DayOfWeek (0 = Sunday)
  time_ticks        bigint not null,   -- TimeSpan ticks
  client_updated_at timestamptz not null,
  deleted_at        timestamptz,
  created_at        timestamptz not null default now(),
  updated_at        timestamptz not null default now()
);

create table public.medication_dose_logs (
  id                uuid primary key,
  medication_id     uuid not null references public.medications (id) on delete cascade,
  pet_id            uuid not null references public.pets (id) on delete cascade,
  scheduled_date    date not null,
  scheduled_time_ticks bigint not null,
  status            text not null,     -- Taken | Skipped | Missed
  resolved_at       timestamptz,
  client_updated_at timestamptz not null,
  deleted_at        timestamptz,
  created_at        timestamptz not null default now(),
  updated_at        timestamptz not null default now(),
  unique (medication_id, scheduled_date, scheduled_time_ticks)
);

create table public.trackers (
  id                uuid primary key,
  pet_id            uuid not null references public.pets (id) on delete cascade,
  tracker_id        text not null,     -- Glucose | Mood | … ([StoreAsText] enum)
  kind              text not null,     -- PerDay | Daily | …
  per_day_count     int not null default 0,
  target_lo         numeric,
  target_hi         numeric,
  unit              text not null default '',
  from_condition    text,
  client_updated_at timestamptz not null,
  deleted_at        timestamptz,
  created_at        timestamptz not null default now(),
  updated_at        timestamptz not null default now(),
  unique (pet_id, tracker_id)          -- one tracker per kind; device seeds converge
);

create table public.pet_conditions (
  id                uuid primary key,
  pet_id            uuid not null references public.pets (id) on delete cascade,
  condition_id      text not null,
  client_updated_at timestamptz not null,
  deleted_at        timestamptz,
  created_at        timestamptz not null default now(),
  updated_at        timestamptz not null default now(),
  unique (pet_id, condition_id)
);

create table public.glucose_entries (
  id                uuid primary key,
  pet_id            uuid not null references public.pets (id) on delete cascade,
  entry_date        date not null,
  time_ticks        bigint not null,
  value             numeric not null,
  food_context      text not null,     -- BeforeFood | AfterFood
  client_updated_at timestamptz not null,
  deleted_at        timestamptz,
  created_at        timestamptz not null default now(),
  updated_at        timestamptz not null default now()
);

create table public.appetite_entries (
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

create table public.seizure_entries (
  id                uuid primary key,
  pet_id            uuid not null references public.pets (id) on delete cascade,
  entry_date        date not null,
  time_ticks        bigint not null,
  duration_minutes  int,
  note              text not null default '',
  client_updated_at timestamptz not null,
  deleted_at        timestamptz,
  created_at        timestamptz not null default now(),
  updated_at        timestamptz not null default now()
);

-- ── updated_at stamping + pull-cursor indexes ───────────────────────────────
-- Server time is the ONLY pull cursor; clients never write updated_at.

create function public.stamp_updated_at()
returns trigger
language plpgsql
as $$
begin
  new.updated_at := now();
  return new;
end $$;

do $$
declare t text;
begin
  foreach t in array array[
    'pets','pet_entries','medications','medication_schedules',
    'medication_dose_logs','trackers','pet_conditions',
    'glucose_entries','appetite_entries','seizure_entries']
  loop
    execute format(
      'create trigger stamp_updated_at before insert or update on public.%I
         for each row execute function public.stamp_updated_at()', t);
    execute format(
      'create index %I on public.%I (updated_at)', t || '_updated_at_idx', t);
  end loop;
end $$;

-- ── RLS for the data tables ─────────────────────────────────────────────────
-- Everything is member-scoped through the pet. medication_schedules has no
-- pet_id, so it routes through its medication.

do $$
declare t text;
begin
  foreach t in array array[
    'pet_entries','medication_dose_logs','trackers','pet_conditions',
    'glucose_entries','appetite_entries','seizure_entries','medications']
  loop
    execute format('alter table public.%I enable row level security', t);
    execute format(
      'create policy "members read"  on public.%I for select using (public.is_pet_member(pet_id))', t);
    execute format(
      'create policy "members insert" on public.%I for insert with check (public.is_pet_member(pet_id))', t);
    execute format(
      'create policy "members update" on public.%I for update using (public.is_pet_member(pet_id))', t);
  end loop;
end $$;

alter table public.medication_schedules enable row level security;

create policy "members read" on public.medication_schedules for select using (
  exists (select 1 from public.medications m
          where m.id = medication_id and public.is_pet_member(m.pet_id)));
create policy "members insert" on public.medication_schedules for insert with check (
  exists (select 1 from public.medications m
          where m.id = medication_id and public.is_pet_member(m.pet_id)));
create policy "members update" on public.medication_schedules for update using (
  exists (select 1 from public.medications m
          where m.id = medication_id and public.is_pet_member(m.pet_id)));

-- ── push_rows: the one write RPC ────────────────────────────────────────────
-- Generic last-write-wins upsert. SECURITY INVOKER on purpose — RLS applies to
-- the caller, so this grants nothing the policies don't. Each table upserts on
-- its natural key (the same uniqueness the app enforces), so two devices that
-- created "the same" row offline converge instead of erroring:
--   pet_entries / appetite_entries    → (pet_id, entry_date)
--   medication_dose_logs              → (medication_id, scheduled_date, scheduled_time_ticks)
--   pet_conditions                    → (pet_id, condition_id)
--   trackers                          → (pet_id, tracker_id)
--   everything else                   → (id)
-- On conflict the incoming row wins only when its client_updated_at is >= the
-- stored one (LWW); `id` and `created_at` are never overwritten, so a
-- natural-key merge keeps the server's canonical id (clients adopt it on pull).
-- Clients MUST send every column (missing jsonb keys read as NULL).

create function public.push_rows(p_table text, p_rows jsonb)
returns void
language plpgsql
security invoker set search_path = ''
as $$
declare
  conflict_cols text;
  set_list text;
begin
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

-- ── account-scoped deletion RPCs ────────────────────────────────────────────

-- "Delete all data" while signed in (the app's full reset): soft-delete every
-- pet the caller OWNS (tombstones so the owner's other devices converge to
-- empty) and remove the caller's caregiver memberships (never touching those
-- pets). SECURITY DEFINER because leaving a membership means deleting a
-- pet_members row, which plain RLS doesn't allow yet.
create function public.delete_my_data()
returns void
language plpgsql
security definer set search_path = ''
as $$
begin
  update public.pets p
     set deleted_at = now(),
         client_updated_at = now()
   where exists (select 1 from public.pet_members m
                 where m.pet_id = p.id
                   and m.user_id = (select auth.uid())
                   and m.role = 'owner')
     and p.deleted_at is null;

  delete from public.pet_members
   where user_id = (select auth.uid()) and role = 'caregiver';
end $$;

-- Account deletion (store policy requires it in-app): hard-delete the auth
-- user; owned pets and all dependent rows go with it via ON DELETE CASCADE
-- (pets themselves cascade from pet_members? No — pets have no FK to users, so
-- delete them explicitly first, then the user row).
create function public.delete_my_account()
returns void
language plpgsql
security definer set search_path = ''
as $$
begin
  delete from public.pets p
   where exists (select 1 from public.pet_members m
                 where m.pet_id = p.id
                   and m.user_id = (select auth.uid())
                   and m.role = 'owner');

  delete from auth.users where id = (select auth.uid());
end $$;

-- Lock the RPCs to signed-in users only.
revoke execute on function public.push_rows(text, jsonb) from anon;
revoke execute on function public.delete_my_data() from anon;
revoke execute on function public.delete_my_account() from anon;
