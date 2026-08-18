-- ═══════════════════════════════════════════════════════════════════════════
--  0018 — Where a creator code came from: typed, or carried by the install.
--
--  0016 assumed one way in: the user opens Settings and types the code. That is
--  the weak half of the funnel — most of a creator's audience never types
--  anything. Google Play preserves a `referrer` value through an install, so a
--  link like
--
--      https://play.google.com/store/apps/details?id=com.felova.app&referrer=creator%3Dtheto
--
--  marks someone with no action on their part at all. Android only: Apple's
--  campaign tokens (pt/ct) reach App Store Connect analytics and are not readable
--  by the app, so on iOS the typed code stays the only in-app path.
--
--  What changes here is ONE COLUMN. The install referrer is a new *input* to the
--  pipeline 0016 already built — same creator_codes, same entries, same
--  last-touch pointer, same webhook attribution — so nothing downstream moves.
--
--  Why the source is recorded rather than inferred: the two arrivals answer
--  different questions and will diverge. An install referrer is FIRST touch
--  (how they found the app, weeks before they cared) and is unambiguous. A typed
--  code is LAST touch and is deliberate — someone chose to credit that person.
--  Keeping both as rows means the attribution rule stays a query, which is the
--  rule 0016 exists to preserve.
-- ═══════════════════════════════════════════════════════════════════════════

-- ── 1. the column ───────────────────────────────────────────────────────────
-- Text with a default rather than an enum, for the reason 0017 spells out: a
-- future client sending a fourth source must not raise a constraint violation
-- that aborts the caller's entire sync against a project this migration has not
-- reached yet.
--
-- Existing rows default to 'typed', which is exactly true: before this migration
-- the only way in was the Settings box.

alter table public.creator_code_entries
  add column if not exists source text not null default 'typed';

comment on column public.creator_code_entries.source is
  'How the code arrived: typed | install_referrer. Recorded, never inferred — an '
  'install referrer is first-touch and automatic, a typed code is last-touch and '
  'deliberate.';

create index if not exists creator_code_entries_source_idx
  on public.creator_code_entries (source, entered_at desc);

-- ── 2. entering, now with a source ──────────────────────────────────────────
-- DROP then CREATE rather than CREATE OR REPLACE: adding a defaulted parameter
-- makes a new overload rather than replacing the old one, and PostgREST calling
-- by named argument would then hit "function is not unique". One signature only.

drop function if exists public.enter_creator_code(text);

create or replace function public.enter_creator_code(p_code text, p_source text default 'typed')
returns text
language plpgsql
security definer set search_path = ''
as $$
declare
  uid uuid;
  found_creator text;
  normalized text;
  src text;
begin
  uid := (select auth.uid());
  if uid is null then
    raise exception 'enter_creator_code: no authenticated user in request context';
  end if;

  normalized := upper(trim(p_code));
  -- Clamped rather than checked: an unknown source is a client that is ahead of
  -- this migration, and losing the label is far better than failing the call.
  src := case when p_source = 'install_referrer' then 'install_referrer' else 'typed' end;

  select c.creator into found_creator
    from public.creator_codes c
   where c.code = normalized and c.active;

  if found_creator is null then
    return null;
  end if;

  if (select count(*) from public.creator_code_entries
      where user_id = uid and entered_at > now() - interval '1 hour') >= 20 then
    return found_creator;   -- accepted, simply not recorded again
  end if;

  -- Deduped per (code, SOURCE), not per code: arriving through a link and later
  -- typing the same code are two genuinely different facts about the same person,
  -- and collapsing them would erase the more interesting one.
  if not exists (
    select 1 from public.creator_code_entries
     where user_id = uid and code = normalized and source = src)
  then
    insert into public.creator_code_entries (user_id, code, creator, source)
    values (uid, normalized, found_creator, src);
  end if;

  -- Last touch wins, regardless of source. An install referrer lands first (first
  -- launch) and a typed code afterwards, so the deliberate act naturally supersedes
  -- the automatic one. An attribution already written at purchase is immutable.
  update public.profiles
     set referred_code = normalized,
         referred_creator = found_creator,
         referred_at = now()
   where id = uid;

  return found_creator;
end $$;

-- ── 3. the report, split by how people arrived ──────────────────────────────
-- Replaces 0016's view. The extra two columns are the whole point of this
-- migration: "the link works, nobody types the code" and "nobody clicks, but the
-- code converts" are opposite problems with opposite fixes, and one blended
-- number hides both.
--
-- DROP then CREATE, not CREATE OR REPLACE. Replacing a view may only APPEND
-- columns — it cannot rename, reorder, or insert one — and the new columns belong
-- next to accounts_entered rather than tacked on after last_purchase, where they
-- would read as an afterthought. CREATE OR REPLACE fails outright here:
--     cannot change name of view column "purchases" to "from_link"
--
-- Plain DROP, never CASCADE: nothing depends on this view today, and if something
-- ever does, failing loudly beats silently dropping it.

drop view if exists public.creator_code_stats;

create view public.creator_code_stats as
  select c.creator,
         min(c.code) filter (where c.active)                as code,
         (select count(distinct e.user_id)
            from public.creator_code_entries e
           where e.creator = c.creator)                     as accounts_entered,
         (select count(distinct e.user_id)
            from public.creator_code_entries e
           where e.creator = c.creator and e.source = 'install_referrer')
                                                            as from_link,
         (select count(distinct e.user_id)
            from public.creator_code_entries e
           where e.creator = c.creator and e.source = 'typed')
                                                            as typed_in,
         (select count(*)
            from public.creator_attributions a
           where a.creator = c.creator)                     as purchases,
         (select max(a.purchased_at)
            from public.creator_attributions a
           where a.creator = c.creator)                     as last_purchase
    from public.creator_codes c
   group by c.creator;

-- ── 4. grants ───────────────────────────────────────────────────────────────
-- Re-granted because the function AND the view were dropped and recreated above;
-- a dropped object takes its grants with it.
--
-- Every statement in this file is idempotent (add column if not exists, drop …
-- if exists, create or replace), so re-running the whole thing after a partial
-- failure is safe and is the intended recovery.

revoke execute on function public.enter_creator_code(text, text) from public, anon;
grant  execute on function public.enter_creator_code(text, text) to authenticated;
revoke all on public.creator_code_stats from anon, authenticated;
