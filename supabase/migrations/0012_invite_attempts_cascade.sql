-- ═══════════════════════════════════════════════════════════════════════════
--  0012 — Make pet_invite_attempts erasable with the account that produced it.
--
--  Corrects an omission in 0006. Every other table either hangs off public.pets
--  or off auth.users with ON DELETE CASCADE, so delete_my_account() removes the
--  lot. pet_invite_attempts was created with a bare `user_id uuid not null` and
--  NO foreign key, because it exists purely to rate-limit redemptions and nothing
--  ever needed to join it. The side effect: after an account deletion its rows
--  survive, each holding the deleted user's UUID and the times they tried to
--  redeem a code.
--
--  That residue identifies nobody once the auth user is gone, but "delete my
--  account" is supposed to mean it, and leftover rows keyed to a former user id
--  are exactly what an erasure review asks about. A foreign key is the right fix
--  rather than a delete inside the RPC: it holds for every future deletion path,
--  including ones that bypass the RPC (a dashboard delete, a support action).
--
--  Rate limiting is unaffected — the window it queries is an hour wide, and rows
--  only vanish when the account they belong to ceases to exist.
-- ═══════════════════════════════════════════════════════════════════════════

-- ── 1. clear the existing orphans ───────────────────────────────────────────
-- Rows whose user is already gone. ADD CONSTRAINT validates existing rows and
-- would fail on any of these, so they must go first — and they are precisely the
-- residue this migration exists to remove.
delete from public.pet_invite_attempts a
 where not exists (
   select 1 from auth.users u where u.id = a.user_id
 );

-- ── 2. the foreign key ──────────────────────────────────────────────────────
-- Matches pet_members.user_id (0001) exactly: references auth.users on delete
-- cascade. From here the auth-user delete in delete_my_account() takes these
-- rows with it, with no change to the RPC.
alter table public.pet_invite_attempts
  add constraint pet_invite_attempts_user_id_fkey
  foreign key (user_id) references auth.users (id) on delete cascade;
