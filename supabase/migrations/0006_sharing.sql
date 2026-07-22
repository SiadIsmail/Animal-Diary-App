-- ═══════════════════════════════════════════════════════════════════════════
--  0006 — Phase 2: multi-caregiver sharing.
--
--  Invite codes (ABCD-1234 shape): owner mints one per pet, another account
--  redeems it and becomes a caregiver; the next sync downloads the pet through
--  the existing RLS scoping — no new data paths. All access goes through
--  SECURITY DEFINER RPCs with named errors (per the debugging philosophy);
--  the invites table has RLS enabled with NO policies and NO role grants, so
--  codes are unreadable outside the RPCs. Redemption is rate-limited with a
--  simple attempts table — deliberately nothing fancier.
-- ═══════════════════════════════════════════════════════════════════════════

create table public.pet_invites (
  id         uuid primary key default gen_random_uuid(),
  pet_id     uuid not null references public.pets (id) on delete cascade,
  code       text not null unique,
  created_by uuid not null references auth.users (id) on delete cascade,
  created_at timestamptz not null default now(),
  expires_at timestamptz not null,
  max_uses   int not null default 1,
  use_count  int not null default 0
);

alter table public.pet_invites enable row level security;   -- deny-all: RPCs only

-- Failed/any redemption attempts, for the rate limit. Deny-all likewise.
create table public.pet_invite_attempts (
  user_id      uuid not null,
  attempted_at timestamptz not null default now()
);

alter table public.pet_invite_attempts enable row level security;

create index pet_invite_attempts_user_time
  on public.pet_invite_attempts (user_id, attempted_at);

-- ── owner mints an invite ───────────────────────────────────────────────────
-- Returns the code. 7-day expiry, single-use. Codes avoid confusable glyphs
-- (no I/O/0/1). Max 10 mints per owner per hour — enough for any real use.

create function public.create_pet_invite(p_pet uuid)
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
begin
  uid := (select auth.uid());
  if uid is null then
    raise exception 'create_pet_invite: no authenticated user in request context';
  end if;

  if not exists (select 1 from public.pet_members
                 where pet_id = p_pet and user_id = uid and role = 'owner') then
    raise exception 'create_pet_invite: caller is not the owner of this pet';
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

-- ── another account redeems it ──────────────────────────────────────────────
-- Unknown and expired codes share one message on purpose (no code probing).

create function public.redeem_invite(p_code text)
returns uuid
language plpgsql
security definer set search_path = ''
as $$
declare
  uid uuid;
  invite public.pet_invites%rowtype;
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

  insert into public.pet_members (pet_id, user_id, role)
  values (invite.pet_id, uid, 'caregiver');

  update public.pet_invites set use_count = use_count + 1 where id = invite.id;

  return invite.pet_id;
end $$;

-- ── member list for the sharing UI ──────────────────────────────────────────
-- Any member may see who else cares for the pet. Emails come from auth.users
-- (definer privilege); they are the only identity the product has.

create function public.list_pet_members(p_pet uuid)
returns table (user_id uuid, member_role text, email text, joined_at timestamptz)
language plpgsql
security definer set search_path = ''
as $$
declare
  uid uuid;
begin
  uid := (select auth.uid());
  if uid is null then
    raise exception 'list_pet_members: no authenticated user in request context';
  end if;
  if not exists (select 1 from public.pet_members
                 where pet_id = p_pet and pet_members.user_id = uid) then
    raise exception 'list_pet_members: caller is not a member of this pet';
  end if;

  return query
    select m.user_id, m.role, coalesce(u.email::text, ''), m.created_at
      from public.pet_members m
      left join auth.users u on u.id = m.user_id
     where m.pet_id = p_pet
     order by m.role, m.created_at;
end $$;

-- ── leave / remove ──────────────────────────────────────────────────────────
-- A caregiver may remove THEMSELF (leave); the owner may remove any caregiver.
-- The owner can never be removed — ownership transfer is a later feature.

create function public.remove_pet_member(p_pet uuid, p_user uuid)
returns void
language plpgsql
security definer set search_path = ''
as $$
declare
  uid uuid;
  target_role text;
begin
  uid := (select auth.uid());
  if uid is null then
    raise exception 'remove_pet_member: no authenticated user in request context';
  end if;

  select role into target_role from public.pet_members
   where pet_id = p_pet and user_id = p_user;
  if target_role is null then
    raise exception 'remove_pet_member: target is not a member of this pet';
  end if;
  if target_role = 'owner' then
    raise exception 'remove_pet_member: the owner cannot be removed';
  end if;

  if p_user <> uid and not exists (
       select 1 from public.pet_members
       where pet_id = p_pet and user_id = uid and role = 'owner') then
    raise exception 'remove_pet_member: only the owner can remove other caregivers';
  end if;

  delete from public.pet_members where pet_id = p_pet and user_id = p_user;
end $$;

-- ── grants: RPCs only ───────────────────────────────────────────────────────

revoke execute on function public.create_pet_invite(uuid) from anon;
revoke execute on function public.redeem_invite(text) from anon;
revoke execute on function public.list_pet_members(uuid) from anon;
revoke execute on function public.remove_pet_member(uuid, uuid) from anon;

grant execute on function public.create_pet_invite(uuid) to authenticated;
grant execute on function public.redeem_invite(text) to authenticated;
grant execute on function public.list_pet_members(uuid) to authenticated;
grant execute on function public.remove_pet_member(uuid, uuid) to authenticated;
