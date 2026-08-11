-- ═══════════════════════════════════════════════════════════════════════════
--  0016 — Creator codes: knowing which influencer a purchase came through.
--
--  A DIFFERENT ANIMAL FROM 0015's ACCESS CODES, and the difference is the whole
--  design. An access code is unique, single-use, and grants a year. A creator
--  code is:
--
--      public      — the creator says it out loud in a video
--      memorable   — "THETO", not FELOVA-K7M2-9XQP
--      reusable    — every one of their followers types the same one
--      inert       — it grants nothing at all (today)
--
--  Because it grants nothing, none of 0015's defences apply: there is no rate
--  limit worth having on a code that hands out no value, and no reason to hide
--  which codes exist. That also means the two must NOT share a table — a bug
--  that let a creator code through the access-code path would hand out a free
--  year to an entire audience.
--
--  Three shapes to preserve:
--
--  1. Every entry is a ROW, and the attribution rule is a QUERY. First-touch vs
--     last-touch, and how long a code stays "live" before a purchase, are
--     decisions that will change once there is real data. Baking either into the
--     write throws away the history needed to change it.
--
--  2. profiles.referred_* is a last-touch POINTER, not the record. It exists so
--     the webhook is one cheap read on the purchase path. The record is
--     creator_code_entries.
--
--  3. Attribution is written at PURCHASE time and is then immutable. Entering a
--     different code afterwards moves the pointer for the NEXT purchase; it can
--     never rewrite an attribution already earned.
--
--  Coverage, stated honestly: this half only sees people who have an account,
--  because the webhook's app_user_id is a Supabase user id only after sign-in
--  (it skips $RCAnonymousID by design). The client also tags the RevenueCat
--  identity with the same code, which covers anonymous buyers in RevenueCat's
--  own reporting. Neither half is complete alone. Report "attributed purchases",
--  never "your purchases".
-- ═══════════════════════════════════════════════════════════════════════════

-- ── 1. the codes ────────────────────────────────────────────────────────────
-- Inserted by the owner from the dashboard. `creator` is shown back to the user
-- ("Thanks. That's Theto's code."), so it is a display name, not a slug.

create table if not exists public.creator_codes (
  code       text primary key,         -- stored and compared UPPER-cased
  creator    text not null,
  active     boolean not null default true,
  note       text,
  created_at timestamptz not null default now()
);

comment on table public.creator_codes is
  'Public, reusable influencer codes. They grant NOTHING; they only attribute a '
  'later purchase. Never join this to access_codes.';

-- ── 2. every entry, ever ────────────────────────────────────────────────────
-- Cascades with the account: this is a per-person record of what someone typed,
-- so "delete my account" must mean it (the same rule 0012 had to retrofit).

create table if not exists public.creator_code_entries (
  id         bigserial primary key,
  user_id    uuid not null references auth.users(id) on delete cascade,
  code       text not null,
  creator    text not null,
  entered_at timestamptz not null default now()
);

create index if not exists creator_code_entries_user_idx
  on public.creator_code_entries (user_id, entered_at desc);
create index if not exists creator_code_entries_creator_idx
  on public.creator_code_entries (creator, entered_at desc);

-- ── 3. the last-touch pointer ───────────────────────────────────────────────

alter table public.profiles
  add column if not exists referred_code    text,
  add column if not exists referred_creator text,
  add column if not exists referred_at      timestamptz;

comment on column public.profiles.referred_code is
  'Last creator code this account entered. A pointer for the webhook to read on '
  'the purchase path; creator_code_entries is the actual record.';

-- ── 4. attributions, written by the webhook at purchase time ────────────────
-- event_id is RevenueCat's own event id and the PRIMARY KEY, which makes the
-- webhook's insert idempotent under the retries migration 0011 exists to survive.
--
-- user_id is ON DELETE SET NULL, not CASCADE, unlike the entries above. Deliberate
-- and worth the inconsistency: once the id is gone the row identifies nobody (a
-- date, a product, a creator), so erasure is honoured — while a creator's earned
-- conversion count does not silently drop months later when an unrelated user
-- deletes their account. Erasure is about the person, not about the fact that a
-- sale happened.
--
-- referred_at travels with the row so an attribution WINDOW can be applied later
-- as a query ("codes entered within 30 days of the purchase") without having
-- needed to guess the right window today.

create table if not exists public.creator_attributions (
  event_id     text primary key,
  user_id      uuid references auth.users(id) on delete set null,
  code         text        not null,
  creator      text        not null,
  event_type   text        not null,
  product_id   text,
  store        text,
  environment  text,
  referred_at  timestamptz,
  purchased_at timestamptz not null default now()
);

create index if not exists creator_attributions_creator_idx
  on public.creator_attributions (creator, purchased_at desc);

-- RLS on, no policies: the definer functions below and the service-role webhook
-- are the only things that touch these.
alter table public.creator_codes         enable row level security;
alter table public.creator_code_entries  enable row level security;
alter table public.creator_attributions  enable row level security;

-- ── 5. looking a code up, WITHOUT an account ────────────────────────────────
-- Granted to `anon` on purpose, and it is the load-bearing grant in this file:
-- the person typing "THETO" has usually just installed the app and has no
-- account, and requiring one to enter a creator code would lose exactly the
-- audience the code exists to measure.
--
-- Nothing leaks. These codes are published by their own creators, and the
-- function returns a display name for a code you already knew, or null. It
-- exposes no user, no purchase, and no billing state, which is why it can be
-- anonymous where owner_has_access can never be.

create or replace function public.lookup_creator_code(p_code text)
returns text
language sql stable
security definer set search_path = ''
as $$
  select c.creator
    from public.creator_codes c
   where c.code = upper(trim(p_code))
     and c.active;
$$;

-- ── 6. recording an entry (signed in) ───────────────────────────────────────
-- Returns the creator name, or null when the code is not a creator code — the
-- caller then tries the access-code path. Never raises for an unknown code: this
-- is a lookup on a shared input box, not a failed redemption.

create or replace function public.enter_creator_code(p_code text)
returns text
language plpgsql
security definer set search_path = ''
as $$
declare
  uid uuid;
  found_creator text;
  normalized text;
begin
  uid := (select auth.uid());
  if uid is null then
    raise exception 'enter_creator_code: no authenticated user in request context';
  end if;

  normalized := upper(trim(p_code));

  select c.creator into found_creator
    from public.creator_codes c
   where c.code = normalized and c.active;

  if found_creator is null then
    return null;
  end if;

  -- A light cap. There is nothing to win by spamming a code that grants nothing,
  -- so this only stops a stuck client from filling the table.
  if (select count(*) from public.creator_code_entries
      where user_id = uid and entered_at > now() - interval '1 hour') >= 20 then
    return found_creator;   -- accepted, simply not recorded again
  end if;

  -- Re-entering the code you already have is a no-op rather than a second row:
  -- it happens naturally when someone reopens the sheet to check it took.
  if not exists (
    select 1 from public.creator_code_entries
     where user_id = uid and code = normalized
     order by entered_at desc limit 1)
  then
    insert into public.creator_code_entries (user_id, code, creator)
    values (uid, normalized, found_creator);
  end if;

  -- Last touch wins. An attribution already written at purchase time is immutable
  -- and unaffected; this only decides where the NEXT purchase is credited.
  update public.profiles
     set referred_code = normalized,
         referred_creator = found_creator,
         referred_at = now()
   where id = uid;

  return found_creator;
end $$;

-- ── 7. the report ───────────────────────────────────────────────────────────
-- One row per creator: how many accounts entered the code, and how many
-- purchases have been credited to it.
--
-- Both halves are counted with independent subqueries rather than a join, for
-- the reason the 0015 view documents: joining two one-to-many tables on the same
-- key multiplies each side by the other's row count.

create or replace view public.creator_code_stats as
  select c.creator,
         min(c.code) filter (where c.active)                as code,
         (select count(distinct e.user_id)
            from public.creator_code_entries e
           where e.creator = c.creator)                     as accounts_entered,
         (select count(*)
            from public.creator_attributions a
           where a.creator = c.creator)                     as purchases,
         (select max(a.purchased_at)
            from public.creator_attributions a
           where a.creator = c.creator)                     as last_purchase
    from public.creator_codes c
   group by c.creator;

-- ── 8. grants ───────────────────────────────────────────────────────────────
-- lookup is anonymous by design (§5); entering requires an account. Minting and
-- reporting stay on the service role, like 0015's.

revoke execute on function public.lookup_creator_code(text) from public;
revoke execute on function public.enter_creator_code(text) from public, anon;
revoke all on public.creator_code_stats from anon, authenticated;

grant execute on function public.lookup_creator_code(text) to anon, authenticated;
grant execute on function public.enter_creator_code(text) to authenticated;
