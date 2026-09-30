-- ═══════════════════════════════════════════════════════════════════════════
--  0015: Access codes: a one-time code that grants a year of full access.
--
--  Assumption this migration relaxes: 0010 shipped exactly two ways to have
--  access, and both are things that happen TO an account rather than things the
--  owner can hand out. `entitlement_active` is written only by the RevenueCat
--  webhook (deliberately: a client-asserted entitlement would let a patched app
--  sponsor unlimited caregivers), and `trial_started_at` is claimed from the
--  device set-if-earlier. Neither can express "this person was given a year".
--
--  What this installs:
--      a code, minted out of band and tagged with the campaign it belongs to,
--      is redeemed once by a signed-in account and grants full access until a
--      date. Nothing is charged, nothing auto-renews, nothing is cancellable.
--
--  Three shapes to preserve:
--
--  1. `granted_until` is its OWN column, not a reuse of entitlement_active. The
--     webhook must stay the only writer of the entitlement columns, and 0011's
--     out-of-order watermark (entitlement_event_at) guards writes that arrive
--     with an event time. A grant has neither property. Keeping them apart also
--     keeps a grant from ever being counted as revenue.
--
--  2. Redeeming a second code RENEWS from that moment; it does not queue behind
--     the running grant. See redeem_access_code for the arithmetic and for the
--     guard that stops a shorter code from cutting a longer grant short.
--
--  3. Attribution outlives the code. access_code_redemptions copies the campaign
--     and references access_codes with ON DELETE RESTRICT, so tidying up an old
--     campaign's leftovers can never silently erase the record of who redeemed
--     it. Deleting an ACCOUNT does cascade its redemption away, which is the
--     erasure rule winning over the analytics, on purpose.
-- ═══════════════════════════════════════════════════════════════════════════

-- ── 1. the grant itself ─────────────────────────────────────────────────────

alter table public.profiles
  add column if not exists granted_until timestamptz;

comment on column public.profiles.granted_until is
  'Full access granted by a redeemed access code, until this instant. Never a '
  'purchase: written only by redeem_access_code, never by the RevenueCat webhook, '
  'and never counted as revenue.';

-- ── 2. the codes ────────────────────────────────────────────────────────────
-- Inserted by the owner, from the dashboard SQL editor or mint_access_codes
-- below. There is no client insert path and no admin UI, deliberately: minting
-- is rare and every code that exists should have been a decision.
--
-- `campaign` is the whole attribution mechanism. It is a property of the CODE
-- ("which giveaway was this minted for"), never of the person who redeems it.

create table if not exists public.access_codes (
  code         text primary key,
  campaign     text        not null,
  grant_length interval    not null default interval '1 year',
  max_uses     int         not null default 1,
  use_count    int         not null default 0,
  expires_at   timestamptz,          -- when the CODE stops being redeemable (null = never)
  note         text,                 -- free text for the owner: who it went to, why
  created_at   timestamptz not null default now()
);

create index if not exists access_codes_campaign_idx
  on public.access_codes (campaign);

-- Who redeemed what. Audit trail, attribution record, and the uniqueness that
-- stops one person burning a multi-use campaign code N times.
create table if not exists public.access_code_redemptions (
  code          text        not null references public.access_codes(code) on delete restrict,
  campaign      text        not null,
  user_id       uuid        not null references auth.users(id) on delete cascade,
  redeemed_at   timestamptz not null default now(),
  granted       interval    not null,
  granted_until timestamptz not null,   -- the resulting expiry, so renewals are legible
  primary key (code, user_id)
);

create index if not exists access_code_redemptions_campaign_idx
  on public.access_code_redemptions (campaign, redeemed_at desc);

-- Attempt log for the rate limit. The cascade to auth.users is NOT optional:
-- pet_invite_attempts shipped without one and needed migration 0012 to fix
-- exactly that. Same table, same lesson, applied up front this time.
create table if not exists public.access_code_attempts (
  id           bigserial primary key,
  user_id      uuid        not null references auth.users(id) on delete cascade,
  attempted_at timestamptz not null default now()
);

create index if not exists access_code_attempts_user_idx
  on public.access_code_attempts (user_id, attempted_at desc);

-- RLS on, no policies at all, on all three. Only the SECURITY DEFINER functions
-- below reach them. A user must never be able to select access_codes and read
-- every unredeemed code in the table.
alter table public.access_codes            enable row level security;
alter table public.access_code_redemptions enable row level security;
alter table public.access_code_attempts    enable row level security;

-- ── 3. redeeming ────────────────────────────────────────────────────────────

create or replace function public.redeem_access_code(p_code text)
returns timestamptz
language plpgsql
security definer set search_path = ''
as $$
declare
  uid uuid;
  c   public.access_codes%rowtype;
  new_until timestamptz;
begin
  uid := (select auth.uid());
  if uid is null then
    raise exception 'redeem_access_code: no authenticated user in request context';
  end if;

  -- Same shape and window as redeem_invite. Codes are guessable in principle, so
  -- this is the only thing between a patient attacker and a free year.
  if (select count(*) from public.access_code_attempts
      where user_id = uid and attempted_at > now() - interval '15 minutes') >= 10 then
    raise exception 'redeem_access_code: too many attempts, wait a while';
  end if;
  insert into public.access_code_attempts (user_id) values (uid);

  select * into c from public.access_codes
   where code = upper(trim(p_code))
   for update;                       -- serializes two devices racing one last use

  -- Wording matters. CloudHttp buckets server text by substring, and
  -- redeem_invite already owns 'invalid or expired code' -> InviteInvalid. Reusing
  -- that phrase here would tell someone their pet INVITE was invalid.
  if c.code is null
     or (c.expires_at is not null and c.expires_at < now())
     or c.use_count >= c.max_uses then
    raise exception 'redeem_access_code: unknown or used access code';
  end if;

  if exists (select 1 from public.access_code_redemptions
             where code = c.code and user_id = uid) then
    raise exception 'redeem_access_code: this access code is already on your account';
  end if;

  -- RENEW from this moment; do not queue behind the running grant. Redeeming a
  -- second one-year code six months in ends the grant a year from today, not
  -- eighteen months from today. The rule reads "a code always buys you a full year
  -- from the day you use it", which is the only version that can be explained in
  -- one sentence to someone in a giveaway thread.
  --
  -- greatest(...) is a GUARD, not the semantic: with every code the same length it
  -- never fires. It exists so a SHORTER code (a 3-month tester comp) redeemed
  -- against a longer running grant cannot silently cut it short. Renewing must
  -- never take time away from someone. Do not "simplify" it away.
  update public.profiles
     set granted_until = greatest(coalesce(granted_until, '-infinity'::timestamptz),
                                  now() + c.grant_length)
   where id = uid
  returning granted_until into new_until;

  if new_until is null then
    raise exception 'redeem_access_code: no profile row for this account';
  end if;

  insert into public.access_code_redemptions (code, campaign, user_id, granted, granted_until)
  values (c.code, c.campaign, uid, c.grant_length, new_until);

  update public.access_codes set use_count = use_count + 1 where code = c.code;

  return new_until;
end $$;

-- ── 4. reading your own grant ───────────────────────────────────────────────
-- The caller's own grant, and nothing else. The trial anchor and the store
-- entitlement are already known on-device, and widening this is how a "just one
-- more field" endpoint becomes a way to read your own (then someone else's)
-- billing state. Compare owner_has_access, which stays internal for the same
-- reason.

create or replace function public.my_access()
returns timestamptz
language sql stable
security definer set search_path = ''
as $$
  select granted_until from public.profiles where id = (select auth.uid());
$$;

-- ── 5. a grant sponsors caregivers, exactly like a subscription ─────────────
-- One added clause. Without it a comped vet's assistant would still be locked
-- out of the pet they were invited to help with.
--
-- No entitlement_grace() on the grant clause: that grace exists because a store
-- renewal is not instantaneous and the RENEWAL webhook lands after the charge. A
-- grant has no renewal, so its expiry is exactly its expiry.

create or replace function public.owner_has_access(p_user uuid)
returns boolean
language sql stable
security definer set search_path = ''
as $$
  select coalesce(
    (select (p.entitlement_active
              and (p.entitlement_expires_at is null
                   or now() < p.entitlement_expires_at + public.entitlement_grace()))
         or (p.granted_until is not null and now() < p.granted_until)
         or (p.trial_started_at is not null
             and now() < p.trial_started_at + public.trial_length())
       from public.profiles p
      where p.id = p_user),
    false);   -- no profile row (admin-deleted auth user) ⇒ false, never null
$$;

-- ── 6. minting a batch ──────────────────────────────────────────────────────
-- Owner-only, from the dashboard. Alphabet is 0010's (no I, O, 0 or 1) because
-- these get read off a screenshot and typed by hand. Shape: FELOVA-XXXX-XXXX.
--
-- The codes are returned by the call that creates them and are not handed back in
-- bulk again; recovering a batch afterwards means querying access_codes by
-- campaign. Copy them out of the results pane.

create or replace function public.mint_access_codes(
  p_campaign text,
  p_count    int,
  p_length   interval    default interval '1 year',
  p_expires  timestamptz default null)
returns setof text
language plpgsql
security definer set search_path = ''
as $$
declare
  letters constant text := 'ABCDEFGHJKLMNPQRSTUVWXYZ';
  digits  constant text := '23456789';
  pool    constant text := letters || digits;
  new_code text;
  made    int := 0;
  attempt int;
begin
  if p_campaign is null or length(trim(p_campaign)) = 0 then
    raise exception 'mint_access_codes: campaign is required (it is the whole attribution)';
  end if;
  if p_count < 1 or p_count > 1000 then
    raise exception 'mint_access_codes: count must be between 1 and 1000';
  end if;

  while made < p_count loop
    attempt := 0;
    loop
      attempt := attempt + 1;
      new_code := 'FELOVA-';
      for i in 1..8 loop
        if i = 5 then
          new_code := new_code || '-';
        end if;
        new_code := new_code || substr(pool, 1 + floor(random() * length(pool))::int, 1);
      end loop;
      begin
        insert into public.access_codes (code, campaign, grant_length, expires_at)
        values (new_code, trim(p_campaign), p_length, p_expires);
        made := made + 1;
        return next new_code;
        exit;
      exception when unique_violation then
        if attempt >= 5 then
          raise exception 'mint_access_codes: could not generate a unique code';
        end if;
      end;
    end loop;
  end loop;
end $$;

-- ── 7. the attribution report ───────────────────────────────────────────────
-- One row per campaign.
--
-- The timestamps are correlated subqueries rather than a join to the redemptions
-- table, deliberately. Joining on campaign fans every code row out by the number
-- of redemptions in that campaign, so count(*) and sum(use_count) both come back
-- multiplied: 50 codes with 10 redemptions would report 500 minted. The aggregate
-- has to be taken over access_codes alone.
--
-- `capacity` (sum of max_uses), not the code count, is the denominator of the
-- rate. They are the same number for single-use codes, and only capacity stays
-- right if a partner ever gets one code with max_uses = 50.

create or replace view public.access_code_stats as
  select c.campaign,
         count(*)                                    as codes,
         coalesce(sum(c.max_uses), 0)                as capacity,
         coalesce(sum(c.use_count), 0)               as redeemed,
         round(100.0 * coalesce(sum(c.use_count), 0)
               / nullif(sum(c.max_uses), 0), 1)      as redeemed_pct,
         (select min(r.redeemed_at) from public.access_code_redemptions r
           where r.campaign = c.campaign)            as first_redeemed,
         (select max(r.redeemed_at) from public.access_code_redemptions r
           where r.campaign = c.campaign)            as last_redeemed
    from public.access_codes c
   group by c.campaign;

-- ── 8. grants ───────────────────────────────────────────────────────────────
-- The app gets exactly two functions: redeem one code, read your own grant.
-- Minting and reporting are owner tools and stay on the service role, which the
-- dashboard already uses; granting them to `authenticated` would let any signed-in
-- user mint themselves a code.

revoke execute on function public.redeem_access_code(text) from anon;
revoke execute on function public.my_access() from anon;
revoke execute on function public.mint_access_codes(text, int, interval, timestamptz)
  from anon, authenticated;
revoke all on public.access_code_stats from anon, authenticated;

grant execute on function public.redeem_access_code(text) to authenticated;
grant execute on function public.my_access() to authenticated;
