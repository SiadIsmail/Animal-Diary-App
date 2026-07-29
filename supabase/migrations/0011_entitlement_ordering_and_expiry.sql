-- ═══════════════════════════════════════════════════════════════════════════
--  0011 — Make the server-side entitlement safe against lost and out-of-order
--  webhooks.
--
--  Assumptions 0010 made that do not hold:
--
--  1. "Webhooks arrive in order." They do not. RevenueCat retries with backoff
--     and gives no ordering guarantee, so a retried EXPIRATION can land AFTER
--     the RENEWAL that superseded it. The later write won, marking a paying
--     owner inactive — and because this column decides who may sponsor, every
--     one of their caregivers silently dropped to read-only until the next
--     renewal. Fixed with an event-time watermark (entitlement_event_at):
--     the webhook ignores any event older than the one already applied. Same
--     last-write-wins shape the sync engine already uses, on event time rather
--     than arrival time.
--
--  2. "EXPIRATION always arrives." If the function is down past RevenueCat's
--     retry budget, it never does, and entitlement_active stays true forever.
--     owner_has_access now also respects entitlement_expires_at, so a missed
--     revocation self-heals at the expiry date instead of granting free
--     sponsorship indefinitely.
-- ═══════════════════════════════════════════════════════════════════════════

-- ── 1. the watermark ────────────────────────────────────────────────────────
-- When the event we last applied was RAISED (not when we processed it).
--
-- NOT NULL with '-infinity' rather than a nullable column, deliberately: it lets
-- the webhook guard every write with one plain `.lte()` filter instead of an
-- `or(is null, lte)`. PostgREST's or() takes a filter STRING in which '.' is
-- reserved, and every ISO timestamp contains dots — so the nullable version puts
-- a parsing hazard on the one code path that must never quietly fail open.
-- Existing rows start at '-infinity', so the first event of any age wins.

alter table public.profiles
  add column if not exists entitlement_event_at timestamptz not null default '-infinity';

comment on column public.profiles.entitlement_event_at is
  'Event timestamp of the last applied RevenueCat webhook. The webhook drops any '
  'event older than this, so retries and out-of-order delivery cannot revive a '
  'stale state. Written only by the revenuecat-webhook function.';

-- ── 2. expiry is now load-bearing, with a grace window ──────────────────────
-- The grace exists because a renewal is not instantaneous: the store charges at
-- period end and the RENEWAL webhook follows, so a strict now() < expires_at
-- would drop a paying owner's caregivers for the minutes (occasionally hours)
-- in between. Two days is comfortably longer than that gap and far shorter than
-- a billing period, so a genuinely lapsed subscription still stops sponsoring
-- promptly.
--
-- A null expiry means "no known end" (lifetime / non-renewing) and stays active.

create or replace function public.entitlement_grace()
returns interval
language sql immutable
set search_path = ''
as $$ select interval '2 days' $$;

create or replace function public.owner_has_access(p_user uuid)
returns boolean
language sql stable
security definer set search_path = ''
as $$
  select coalesce(
    (select (p.entitlement_active
              and (p.entitlement_expires_at is null
                   or now() < p.entitlement_expires_at + public.entitlement_grace()))
         or (p.trial_started_at is not null
             and now() < p.trial_started_at + public.trial_length())
       from public.profiles p
      where p.id = p_user),
    false);   -- no profile row (admin-deleted auth user) ⇒ false, never null
$$;

-- ── 3. grants ───────────────────────────────────────────────────────────────
-- owner_has_access stays internal: it is called by the definer functions that
-- already check membership. Granting it broadly would let any signed-in user
-- probe any other user's billing state.

revoke execute on function public.owner_has_access(uuid) from anon, authenticated;
revoke execute on function public.entitlement_grace() from anon;
