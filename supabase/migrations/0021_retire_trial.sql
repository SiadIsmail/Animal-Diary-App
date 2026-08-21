-- ═══════════════════════════════════════════════════════════════════════════
--  0021 — Retire the trial.
--
--  The paid boundary was inverted: writing things down is free forever, and what
--  is paid for is what the accumulated record is FOR (the assembled appointment
--  summary, the designed vet report, backup, a second pet, minting an invite).
--  The 14-day trial is gone from the app entirely — TrialService, ITrialStore,
--  ITrialAnchor, BillingConfig.TrialLength and AccessState.Trial/TrialExpired all
--  deleted — so the server half has to go with it or it becomes a clause nobody
--  can explain.
--
--  Server-side, owner_has_access answered:
--      entitlement_active(+grace)  OR  grant_active  OR  inside the 14-day trial
--  and becomes:
--      entitlement_active(+grace)  OR  grant_active
--
--  WHAT THIS TAKES AWAY, AND FROM WHOM. This function's only job is telling a
--  caregiver whether the owner of a pet they help with is covered. Dropping the
--  trial clause means every owner on the new free tier stops sponsoring. That is
--  intended — a free tier that sponsors unlimited caregivers is not a tier — and
--  it applies to everyone at once, including people already caregiving for a free
--  owner. Nobody is grandfathered, deliberately.
--
--  That is affordable because RevenueCat has only ever run on the Test Store key,
--  so no purchase has ever completed and the affected population is a handful of
--  installs. And because of what sponsorship now reaches: with logging free on
--  every tier, an unsponsored caregiver keeps reading everything and keeps writing
--  everything down. What they lose is the paid pet-scoped surfaces, which is a
--  subscribe prompt, not a loss of care or of data.
--
--  Nothing here touches medical data, and nothing here can gate a write: the app
--  never asked the server for permission to log, and after this change nothing
--  gates logging at all.
-- ═══════════════════════════════════════════════════════════════════════════

-- ── 1. access without the trial arm ─────────────────────────────────────────
-- Same shape as 0015's version, one clause shorter. Still SECURITY DEFINER
-- because callers ask about OTHER users (a caregiver asking about their pet's
-- owner), still returning a bare boolean and nothing else. That narrow return
-- type is the whole privacy contract of the feature; do not widen it.
--
-- entitlement_grace() stays on the entitlement clause only. It exists because a
-- store renewal is not instantaneous and the RENEWAL webhook lands after the
-- charge. A grant has no renewal, so its expiry is exactly its expiry.

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
       from public.profiles p
      where p.id = p_user),
    false);   -- no profile row (admin-deleted auth user) ⇒ false, never null
$$;

-- Unchanged from 0010/0011/0015: internal only. It is called by the definer
-- functions that need it and must never become a way to read another person's
-- billing state directly.
revoke execute on function public.owner_has_access(uuid) from anon, authenticated;

-- ── 2. the anchor, and the RPC that maintained it ───────────────────────────
-- claim_trial_anchor was the client's only writer for trial_started_at, called
-- once per sync from CloudSyncService.ReconcileTrialAnchorAsync. Both are gone
-- from the app in the same change. Dropping the function BEFORE the column
-- because it references it.
--
-- A client still running the previous build will call an RPC that no longer
-- exists. That is safe by construction: the call was already wrapped so that a
-- failure could not abort the sync cycle ("the anchor is not needed to move pet
-- data"), so an old build keeps syncing pet records normally and simply stops
-- claiming an anchor nothing reads any more.

drop function if exists public.claim_trial_anchor(timestamptz);

alter table public.profiles
  drop column if exists trial_started_at;

-- trial_length() had exactly one caller, the clause removed above. It was
-- duplicated from BillingConfig.TrialLength deliberately (the client needed it
-- offline, the server needed it to answer for OTHER users); with neither side
-- having a trial, leaving it behind would leave a "14 days" constant in the
-- schema that means nothing.
drop function if exists public.trial_length();
