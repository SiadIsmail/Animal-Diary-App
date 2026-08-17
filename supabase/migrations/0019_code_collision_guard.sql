-- ═══════════════════════════════════════════════════════════════════════════
--  0019 — Stop a creator code and an access code sharing the same text.
--
--  0015 and 0016 kept the two kinds of code in separate tables, which is right:
--  one grants a year, the other grants nothing, and a routing bug between them
--  would hand a free year to an entire audience. But separate tables means
--  neither knows about the other, and BOTH are ultimately hand-inserted by the
--  owner. Nothing stopped 'THETO' existing in each.
--
--  The failure is silent and lands on the user, not on us. The app deliberately
--  tries the creator lookup FIRST (it is cheap and spends none of
--  redeem_access_code's attempt budget), so a duplicated code would resolve as
--  the creator code every time: someone told they were getting a free year would
--  see "Thanks, we'll know you came from Theto" and get nothing. No error, no log,
--  no way for them to tell it went wrong.
--
--  Triggers rather than a shared table or a composite unique index, because the
--  separation is the safety property and merging the tables to enforce it would
--  trade a real defence for a cosmetic one. This just makes the second insert
--  fail loudly, at the moment the owner types it, which is the only moment anyone
--  can fix it.
-- ═══════════════════════════════════════════════════════════════════════════

-- ── 1. the two guards ───────────────────────────────────────────────────────
-- Both normalize the way their own table does: access codes are stored as typed
-- but compared upper-cased+trimmed by redeem_access_code, and creator codes are
-- stored upper-cased. Comparing upper(trim(...)) on both sides catches a
-- collision that differs only in case, which is the likely way to create one.

create or replace function public.reject_code_collision()
returns trigger
language plpgsql
security definer set search_path = ''
as $$
declare
  other text;
begin
  if tg_table_name = 'creator_codes' then
    select a.code into other from public.access_codes a
     where upper(trim(a.code)) = upper(trim(new.code));
    if other is not null then
      raise exception
        'creator_codes: % is already an ACCESS code (grants a year). Pick a different creator code.',
        new.code;
    end if;
  else
    select c.code into other from public.creator_codes c
     where upper(trim(c.code)) = upper(trim(new.code));
    if other is not null then
      raise exception
        'access_codes: % is already a CREATOR code (grants nothing). Pick a different access code.',
        new.code;
    end if;
  end if;
  return new;
end $$;

drop trigger if exists creator_codes_no_collision on public.creator_codes;
create trigger creator_codes_no_collision
  before insert or update of code on public.creator_codes
  for each row execute function public.reject_code_collision();

drop trigger if exists access_codes_no_collision on public.access_codes;
create trigger access_codes_no_collision
  before insert or update of code on public.access_codes
  for each row execute function public.reject_code_collision();

-- ── 2. surface any collision that already exists ────────────────────────────
-- The triggers only guard NEW rows. If the two tables already disagree, this
-- raises at migration time so it is fixed now rather than discovered by a user
-- who was promised a free year.

do $$
declare
  dupes text;
begin
  select string_agg(c.code, ', ') into dupes
    from public.creator_codes c
    join public.access_codes a on upper(trim(a.code)) = upper(trim(c.code));

  if dupes is not null then
    raise exception
      'Existing code collisions between creator_codes and access_codes: %. '
      'Remove or rename one side, then re-run this migration.', dupes;
  end if;
end $$;

revoke execute on function public.reject_code_collision() from anon, authenticated;
