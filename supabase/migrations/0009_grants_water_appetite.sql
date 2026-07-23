-- ═══════════════════════════════════════════════════════════════════════════
--  0009 — role grants for the water + appetite tables.
--
--  Corrects an omission in 0007 / 0008: those migrations created new data tables
--  with RLS policies but no base-table privileges for `authenticated`. RLS
--  decides which ROWS a role sees; a GRANT decides whether the role MAY touch the
--  table at all — both are required (same lesson as 0002). Without this, the pull
--  side's PostgREST SELECT fails with "42501 permission denied for table …",
--  which aborts the entire sync run.
--
--  Mirrors 0002 exactly: `authenticated` only, select/insert/update only (the app
--  soft-deletes; hard-delete goes through SECURITY DEFINER RPCs). GRANT is
--  idempotent, so re-running is safe.
-- ═══════════════════════════════════════════════════════════════════════════

grant select, insert, update on public.water_amount_entries    to authenticated;
grant select, insert, update on public.water_level_entries     to authenticated;
grant select, insert, update on public.appetite_amount_entries to authenticated;
