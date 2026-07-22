-- ═══════════════════════════════════════════════════════════════════════════
--  0003 — fix the pets INSERT policy.
--
--  The push RPC failed with `new row violates row-level security policy for
--  table "pets"` — the INSERT arm's WITH CHECK ((select auth.uid()) is not
--  null) is the only check on that path. The auth.uid() test was redundant
--  belt-and-braces anyway: since 0002, ONLY the `authenticated` role holds
--  INSERT privilege on pets (anon has zero grants), so scoping the policy TO
--  authenticated with CHECK (true) enforces exactly the same rule — a signed-in
--  user may create pets — without consulting auth.uid() on this path at all.
--
--  The ownership trigger keeps its auth.uid() dependency (it must — the owner
--  membership row needs the user id), but now fails LOUDLY with a named error
--  if the auth context is ever genuinely missing, instead of surfacing as a
--  cryptic constraint/RLS failure downstream.
-- ═══════════════════════════════════════════════════════════════════════════

drop policy "pets: signed-in insert" on public.pets;

create policy "pets: signed-in insert" on public.pets
  for insert to authenticated
  with check (true);

create or replace function public.handle_new_pet()
returns trigger
language plpgsql
security definer set search_path = ''
as $$
declare
  uid uuid;
begin
  uid := (select auth.uid());
  if uid is null then
    raise exception 'handle_new_pet: no authenticated user in request context';
  end if;
  insert into public.pet_members (pet_id, user_id, role)
  values (new.id, uid, 'owner');
  return new;
end $$;
