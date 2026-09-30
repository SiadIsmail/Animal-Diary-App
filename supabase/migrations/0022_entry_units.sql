-- ═══════════════════════════════════════════════════════════════════════════
--  0022: the unit an entry was written in.
--
--  An owner asked to record weight in pounds. Weight is not the only place this
--  bites (a diabetic pet in the US needs mg/dL, a rat needs grams, a US owner
--  measures water in cups), so the unit becomes a property of the ENTRY rather
--  than a setting, and this migration is the storage half of that.
--
--  ONE COLUMN PER TABLE AND NOTHING ELSE. No trigger changes, no RLS changes, no
--  new grants: every table below already carries stamp_updated_at, the three
--  member-scoped policies and the grant to `authenticated`. push_rows needs no
--  redefinition either, because it derives its column list from
--  information_schema.columns (see 0013/0014/0017), so a new column is picked up
--  on the next call.
--
--  WHAT THE COLUMN IS, AND WHAT IT IS NOT.
--
--  The stored VALUE stays canonical: kilograms, mmol/L, millilitres, grams. This
--  column records what the owner typed in. It is PROVENANCE, not the reading.
--  That distinction is load-bearing: every aggregate in the app (the report's
--  daily sums, the charts, the recorded lowest/highest) operates on one comparable
--  number and keeps working untouched. Storing the raw entered value instead would
--  break every one of them, silently, on data already in the cloud.
--
--  NULLABLE, WITH NO DEFAULT AND NO BACKFILL, exactly like 0017's seizure_type.
--  NULL means "the canonical unit", which is true of every row written before
--  today, so there is nothing to migrate and no window where a row means something
--  different from what it meant yesterday.
--
--  TEXT, not an enum type and not a CHECK constraint, for the reason 0017 gives at
--  length: a CHECK here would mean a future client sending a unit this project has
--  not heard of raises a constraint violation that aborts that caller's ENTIRE
--  sync, every table, not just this one. The client already reads an unrecognised
--  id as the canonical unit, which is what the number is anyway. The ids in
--  Data/Models/Units.cs are the wire format ('kg', 'lb', 'g', 'mmol_l', 'mg_dl',
--  'ml', 'fl_oz', 'cup', 'oz', 's', 'min'); renaming one there orphans every row
--  here.
--
--  custom_entries.unit is the odd one out and deliberately so: it holds the
--  owner's own free text ("min", "km", "poops") copied from the tracker definition
--  at the moment the entry was written. Nothing converts it. It exists because
--  renaming a tracker's unit used to retroactively relabel every entry already
--  recorded, which restated 35 minutes of walking as 35 kilometres.
--
--  Ordering note, the same trade 0014 and 0017 accepted: shipping the client before
--  this is applied is safe (jsonb_populate_recordset drops keys the table does not
--  have, so the unit is simply lost until the column exists). The reverse, an OLD
--  client pushing an update to a row that already carries a unit, nulls the column,
--  because the upsert's SET list writes every column from the incoming record. A
--  nulled unit reads as canonical, so the reading itself is never wrong: only the
--  label the owner chose is lost, and only for rows an old device edits.
-- ═══════════════════════════════════════════════════════════════════════════

-- Weight lives on the mood+weight row, so the column names the value it describes:
-- a mood has no unit and never will.
alter table public.pet_entries
  add column if not exists weight_unit text;

alter table public.glucose_entries
  add column if not exists unit text;

alter table public.water_amount_entries
  add column if not exists unit text;

alter table public.appetite_amount_entries
  add column if not exists unit text;

alter table public.seizure_entries
  add column if not exists unit text;

alter table public.custom_entries
  add column if not exists unit text;

comment on column public.pet_entries.weight_unit is
  'Unit the owner typed the weight in: kg | lb | g. NULL means kg. '
  'Provenance only: the stored weight is always kilograms.';

comment on column public.glucose_entries.unit is
  'Unit the owner typed the reading in: mmol_l | mg_dl. NULL means mmol/L. '
  'Provenance only: the stored value is always mmol/L.';

comment on column public.water_amount_entries.unit is
  'Unit the owner typed the amount in: ml | fl_oz | cup. NULL means ml. '
  'Provenance only: the stored amount is always millilitres.';

comment on column public.appetite_amount_entries.unit is
  'Unit the owner typed the amount in: g | oz. NULL means grams. '
  'Provenance only: the stored amount is always grams.';

comment on column public.seizure_entries.unit is
  'Unit the owner typed the duration in: s | min. NULL means the canonical unit. '
  'Provenance only: the stored duration is always canonical.';

comment on column public.custom_entries.unit is
  'The owner-defined tracker''s own free-text unit as it stood when this entry was '
  'written ("min", "km", "poops"). NEVER converted: nothing can convert it. Stored '
  'so renaming a tracker''s unit cannot retroactively relabel history.';
