-- ═══════════════════════════════════════════════════════════════════════════
--  0014: does an owner-defined tracker reach the vet summary?
--
--  Corrects an omission in 0013, which shipped custom_trackers with no way to say
--  this. It has to be a column, and it has to sync: only the OWNER can know whether
--  a thing is clinical (a walk is noise for one household and the whole point for
--  another whose dog has a limp), and a caregiver exporting the summary from their
--  own device must honour the same answer.
--
--  It lives on the tracker rather than on each entry (that would be the same
--  question 200 times) or on each export (the answer does not change between them).
--
--  Default true, matching the client: the two failure modes are not symmetric. A
--  stray "Walk" section is something a vet skims past; a tracker of vomiting that
--  silently never reached the summary is the app failing at the appointment where
--  it mattered.
--
--  push_rows needs no change: it derives its column list from
--  information_schema.columns, so a new column is picked up automatically.
-- ═══════════════════════════════════════════════════════════════════════════

alter table public.custom_trackers
  add column include_in_report boolean not null default true;
