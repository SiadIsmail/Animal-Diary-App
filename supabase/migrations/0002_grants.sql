-- ═══════════════════════════════════════════════════════════════════════════
--  0002 — role grants. Newer Supabase projects no longer auto-grant table
--  privileges to the API roles for tables created in the SQL editor, so RLS
--  policies alone yield "42501 permission denied". Grants say what a role MAY
--  attempt; RLS still decides which rows — both are required.
--
--  Only `authenticated` gets anything: every app call runs with a user token,
--  and signed-out devices make no data calls at all — `anon` stays at zero.
--  No DELETE grants anywhere: the app soft-deletes, and the hard-deleting RPCs
--  are SECURITY DEFINER (they run as their owner, not the caller).
-- ═══════════════════════════════════════════════════════════════════════════

grant usage on schema public to authenticated;

grant select on public.profiles    to authenticated;
grant select on public.pet_members to authenticated;

grant select, insert, update on public.pets                 to authenticated;
grant select, insert, update on public.pet_entries          to authenticated;
grant select, insert, update on public.medications          to authenticated;
grant select, insert, update on public.medication_schedules to authenticated;
grant select, insert, update on public.medication_dose_logs to authenticated;
grant select, insert, update on public.trackers             to authenticated;
grant select, insert, update on public.pet_conditions       to authenticated;
grant select, insert, update on public.glucose_entries      to authenticated;
grant select, insert, update on public.appetite_entries     to authenticated;
grant select, insert, update on public.seizure_entries      to authenticated;

grant execute on function public.push_rows(text, jsonb) to authenticated;
grant execute on function public.delete_my_data()       to authenticated;
grant execute on function public.delete_my_account()    to authenticated;
