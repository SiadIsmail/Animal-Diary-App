-- ═══════════════════════════════════════════════════════════════════════════
--  0017 — what kind of seizure it was.
--
--  An optional attribute of a seizure that was already logged, so it is a column
--  on seizure_entries rather than a table: nothing about it is a separate thing
--  to record, and it can never exist without the occurrence it describes.
--
--  NULLABLE, WITH NO DEFAULT, and that is the whole design. An owner who does not
--  know the term picks nothing, and "nothing" has to stay distinguishable from
--  every real answer — so there is no 'unknown' member and no backfill. Every row
--  written before today is null because nobody was asked, which is exactly true.
--
--  Text, not an enum type or a check constraint, deliberately. The client already
--  parses defensively (an unrecognised value reads back as "not said"), and a CHECK
--  here would mean a future client sending a fourth type raises a constraint
--  violation that aborts the caller's ENTIRE sync — every table, not just this one —
--  against a project this migration had not reached yet. A marketing-grade taxonomy
--  is not worth that blast radius. The member names in Data/Models/JournalEntries.cs
--  are the wire format ('Generalized', 'Focal', 'FocalToGeneralized'); renaming one
--  there orphans every row here.
--
--  push_rows needs no change: it derives its column list from
--  information_schema.columns (see 0013/0014), so the new column is picked up on
--  the next call with no redefinition.
--
--  Ordering note: shipping the client before this migration is applied is safe —
--  jsonb_populate_recordset drops keys the table does not have, so the value is
--  simply lost until the column exists. The reverse (an OLD client pushing an
--  update to a row that already carries a type) nulls the column, because the
--  upsert's SET list writes every column from the incoming record. Seizure entries
--  are only ever inserted or soft-deleted, never edited, so the window is a delete
--  from a stale device — the same trade 0014 accepted for include_in_report.
-- ═══════════════════════════════════════════════════════════════════════════

alter table public.seizure_entries
  add column if not exists seizure_type text;

comment on column public.seizure_entries.seizure_type is
  'What the owner said the seizure was: Generalized | Focal | FocalToGeneralized. '
  'NULL means they were not asked or did not say, and is never a guess.';
