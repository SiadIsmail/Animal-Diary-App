-- ═══════════════════════════════════════════════════════════════════════════
--  0010: Sponsored caregiver access.
--
--  Assumption this migration fixes: 0001 shipped `profiles.plan` as a
--  placeholder for a future paid tier, and nothing ever read it. Billing lived
--  entirely on-device (a local trial clock + a RevenueCat entitlement keyed to
--  an anonymous per-install id), so the server had no way to answer the one
--  question sharing needs: "does this pet's OWNER currently have access?"
--  Without that, every caregiver had to buy their own subscription to log care
--  on someone else's animal.
--
--  The rule this installs:
--      you may write to a pet if you have your own access,
--      OR you are a CAREGIVER on it and its owner has access.
--
--  Sponsorship never reaches a pet you own yourself. That is the structural
--  reason one subscription cannot be laundered into unlimited free accounts,
--  and it is why the caps below are a backstop rather than the defence.
--
--  `profiles.plan` is left untouched and still unused; it is superseded by
--  entitlement_active/entitlement_expires_at here. Removing it is a separate
--  cleanup, deliberately not bundled into a behaviour change.
-- ═══════════════════════════════════════════════════════════════════════════

-- ── 1. the owner-side access facts ──────────────────────────────────────────
-- Two independent sources, exactly mirroring the client's own gate:
--   trial_started_at  : the app-side trial, claimed from the device (below)
--   entitlement_active: the store subscription, written ONLY by the RevenueCat
--                        webhook (service role). Never written by the client:
--                        a client-asserted entitlement would let a patched app
--                        sponsor an unlimited number of caregivers.

alter table public.profiles
  add column if not exists trial_started_at      timestamptz,
  add column if not exists entitlement_active    boolean not null default false,
  add column if not exists entitlement_expires_at timestamptz,
  add column if not exists entitlement_updated_at timestamptz;

-- The trial length must match BillingConfig.TrialLength on the client. It is
-- duplicated deliberately: the client needs it offline, the server needs it to
-- answer for OTHER users. A single function keeps the server half in one place.
create or replace function public.trial_length()
returns interval
language sql immutable
set search_path = ''
as $$ select interval '14 days' $$;

-- ── 2. does this user currently have access? ────────────────────────────────
-- SECURITY DEFINER because callers ask about OTHER users (a caregiver asking
-- about their pet's owner). It returns a bare boolean and nothing else, never
-- the plan, the expiry, or the email. That is the whole privacy contract of
-- this feature; do not widen the return type.

create or replace function public.owner_has_access(p_user uuid)
returns boolean
language sql stable
security definer set search_path = ''
as $$
  select coalesce(
    (select p.entitlement_active
         or (p.trial_started_at is not null
             and now() < p.trial_started_at + public.trial_length())
       from public.profiles p
      where p.id = p_user),
    false);   -- no profile row (admin-deleted auth user) ⇒ false, never null
$$;

-- ── 3. the caller's memberships, with the sponsorship bit ───────────────────
-- Replaces the client's plain `pet_members?select=pet_id,role` read. Same shape
-- plus one boolean, so it still drives BOTH the sharing UI's role display and
-- the sync's purge of pets whose membership is gone.
--
-- owner_access is reported for every row (including pets you own, where it is
-- simply your own state); the CLIENT ignores it on owned pets, because your own
-- access is already the answer there.

-- carer_count is the number of CAREGIVERS on the pet (the owner is not counted). The
-- client uses it for one thing only: when an owner's own access ends, telling them that
-- the people helping with this pet just went read-only too. No membership detail leaks,
-- any member can already list the others via list_pet_members.

create or replace function public.list_my_pet_access()
returns table (pet_id uuid, member_role text, owner_access boolean, carer_count int)
language plpgsql
security definer set search_path = ''
as $$
declare
  uid uuid;
begin
  uid := (select auth.uid());
  if uid is null then
    raise exception 'list_my_pet_access: no authenticated user in request context';
  end if;

  return query
    select m.pet_id,
           m.role,
           public.owner_has_access(owner.user_id),
           (select count(*)::int from public.pet_members c
             where c.pet_id = m.pet_id and c.role = 'caregiver')
      from public.pet_members m
      left join public.pet_members owner
             on owner.pet_id = m.pet_id and owner.role = 'owner'
     where m.user_id = uid;
end $$;

-- ── 4. the trial anchor, claimed from the device ────────────────────────────
-- The app-side trial starts when the user creates their first OWN pet, which is
-- a local event the server cannot observe. The client reports it; this claims it
-- set-if-null and hands back the effective value.
--
-- Monotone by construction: coalesce never overwrites an existing anchor, so a
-- second device, a reinstall, or a fresh sign-in can only ever CONFIRM the
-- account's trial start, never push it later and mint a new sponsorship window.
-- A null argument claims nothing (someone who has never started a trial).

create or replace function public.claim_trial_anchor(p_started timestamptz)
returns timestamptz
language plpgsql
security definer set search_path = ''
as $$
declare
  uid uuid;
  effective timestamptz;
begin
  uid := (select auth.uid());
  if uid is null then
    raise exception 'claim_trial_anchor: no authenticated user in request context';
  end if;

  -- Write only when the device actually has an anchor AND it predates whatever is
  -- stored. Every other case falls through to a plain read-back below.
  update public.profiles
     set trial_started_at = p_started
   where id = uid
     and p_started is not null
     and (trial_started_at is null or p_started < trial_started_at)
  returning trial_started_at into effective;

  if effective is null then
    select trial_started_at into effective from public.profiles where id = uid;
  end if;

  return effective;
end $$;

-- ── 5. caregiver caps ───────────────────────────────────────────────────────
-- Checked in BOTH invite RPCs on purpose. redeem_invite is the real enforcement
-- (a slot can fill between minting and redeeming), but a failure there surfaces
-- to the CAREGIVER, who can do nothing about it. Checking at mint time too means
-- the owner (the only person who can act) hears about it first.
--
-- Never retroactive: an owner already over a cap keeps everyone they have.

create or replace function public.caregiver_counts(p_pet uuid, p_owner uuid)
returns table (on_pet int, for_owner int)
language sql stable
security definer set search_path = ''
as $$
  select
    (select count(*)::int from public.pet_members
      where pet_id = p_pet and role = 'caregiver'),
    (select count(*)::int
       from public.pet_members c
       join public.pet_members o
         on o.pet_id = c.pet_id and o.role = 'owner' and o.user_id = p_owner
      where c.role = 'caregiver');
$$;

create or replace function public.create_pet_invite(p_pet uuid)
returns text
language plpgsql
security definer set search_path = ''
as $$
declare
  uid uuid;
  letters constant text := 'ABCDEFGHJKLMNPQRSTUVWXYZ';
  digits  constant text := '23456789';
  new_code text;
  attempt int := 0;
  counts record;
begin
  uid := (select auth.uid());
  if uid is null then
    raise exception 'create_pet_invite: no authenticated user in request context';
  end if;

  if not exists (select 1 from public.pet_members
                 where pet_id = p_pet and user_id = uid and role = 'owner') then
    raise exception 'create_pet_invite: caller is not the owner of this pet';
  end if;

  select * into counts from public.caregiver_counts(p_pet, uid);
  if counts.on_pet >= 5 then
    raise exception 'create_pet_invite: this pet already has the maximum number of carers';
  end if;
  if counts.for_owner >= 10 then
    raise exception 'create_pet_invite: you already have the maximum number of carers';
  end if;

  if (select count(*) from public.pet_invites
      where created_by = uid and created_at > now() - interval '1 hour') >= 10 then
    raise exception 'create_pet_invite: too many invites created, wait a while';
  end if;

  loop
    attempt := attempt + 1;
    new_code :=
      substr(letters, 1 + floor(random() * 24)::int, 1) ||
      substr(letters, 1 + floor(random() * 24)::int, 1) ||
      substr(letters, 1 + floor(random() * 24)::int, 1) ||
      substr(letters, 1 + floor(random() * 24)::int, 1) || '-' ||
      substr(digits, 1 + floor(random() * 8)::int, 1) ||
      substr(digits, 1 + floor(random() * 8)::int, 1) ||
      substr(digits, 1 + floor(random() * 8)::int, 1) ||
      substr(digits, 1 + floor(random() * 8)::int, 1);
    begin
      insert into public.pet_invites (pet_id, code, created_by, expires_at)
      values (p_pet, new_code, uid, now() + interval '7 days');
      return new_code;
    exception when unique_violation then
      if attempt >= 5 then
        raise exception 'create_pet_invite: could not generate a unique code';
      end if;
    end;
  end loop;
end $$;

create or replace function public.redeem_invite(p_code text)
returns uuid
language plpgsql
security definer set search_path = ''
as $$
declare
  uid uuid;
  invite public.pet_invites%rowtype;
  pet_owner uuid;
  counts record;
begin
  uid := (select auth.uid());
  if uid is null then
    raise exception 'redeem_invite: no authenticated user in request context';
  end if;

  if (select count(*) from public.pet_invite_attempts
      where user_id = uid and attempted_at > now() - interval '15 minutes') >= 10 then
    raise exception 'redeem_invite: too many attempts, wait a while';
  end if;
  insert into public.pet_invite_attempts (user_id) values (uid);

  select * into invite from public.pet_invites
   where code = upper(trim(p_code))
   for update;

  if invite.id is null or invite.expires_at < now() or invite.use_count >= invite.max_uses then
    raise exception 'redeem_invite: invalid or expired code';
  end if;

  if exists (select 1 from public.pet_members
             where pet_id = invite.pet_id and user_id = uid) then
    raise exception 'redeem_invite: already a member of this pet';
  end if;

  select user_id into pet_owner from public.pet_members
   where pet_id = invite.pet_id and role = 'owner';

  select * into counts from public.caregiver_counts(invite.pet_id, pet_owner);
  if counts.on_pet >= 5 then
    raise exception 'redeem_invite: this pet already has the maximum number of carers';
  end if;
  if counts.for_owner >= 10 then
    raise exception 'redeem_invite: this owner already has the maximum number of carers';
  end if;

  insert into public.pet_members (pet_id, user_id, role)
  values (invite.pet_id, uid, 'caregiver');

  update public.pet_invites set use_count = use_count + 1 where id = invite.id;

  return invite.pet_id;
end $$;

-- ── 6. grants: RPCs only, authenticated only ────────────────────────────────
-- owner_has_access and caregiver_counts are INTERNAL helpers: they are called by
-- the definer functions above, which run as the owner, so no role needs execute
-- on them directly. Granting them to `authenticated` would let any signed-in
-- user probe any other user's billing state.

revoke execute on function public.owner_has_access(uuid) from anon, authenticated;
revoke execute on function public.caregiver_counts(uuid, uuid) from anon, authenticated;
revoke execute on function public.trial_length() from anon;

revoke execute on function public.list_my_pet_access() from anon;
revoke execute on function public.claim_trial_anchor(timestamptz) from anon;

grant execute on function public.list_my_pet_access() to authenticated;
grant execute on function public.claim_trial_anchor(timestamptz) to authenticated;
