# Project history

Point-in-time documents: the plans that shaped a subsystem before it was built,
the audits that reviewed one afterwards, and one incident narrative.

**None of these describes current behaviour.** They are kept for the *why* —
the constraint that forced a shape, the bug that motivated a rule, the option
that was considered and rejected. When one of them disagrees with the code, the
code is right.

For how the app works today, read `AI/` (structure, domain rules, decisions,
conventions) and the code itself.

| Document | What it is | Subsystem |
|---|---|---|
| [CLOUD_SYNC_PLAN.md](CLOUD_SYNC_PLAN.md) | Design plan, written before the work. Built (phases 0–2). | Cloud |
| [ACCOUNT_LIFECYCLE_PLAN.md](ACCOUNT_LIFECYCLE_PLAN.md) | Incident narrative + fix plan after a caregiver-testing data-loss. Built. | Cloud |
| [MONETIZATION_PLAN.md](MONETIZATION_PLAN.md) | Design plan, written before the work. Built. | Billing |
| [BILLING_AUDIT.md](BILLING_AUDIT.md) | Review of the billing stack against RevenueCat / Play Billing docs, with a remediation log. | Billing |
| [NOTIFICATION_AUDIT.md](NOTIFICATION_AUDIT.md) | Review of the whole local-notification subsystem, with a remediation log. | Notifications |

## Reading an audit

The two audits are written against the **pre-fix** code and kept that way
deliberately — the finding explains what was wrong, and the remediation table at
the top says what happened to it. A finding marked *Fixed* describes code that no
longer exists; a finding marked *Won't fix* is a live, accepted trade-off and will
also appear in `AI/known-constraints.md`.

Neither audit's counts (findings, tests, warnings) are maintained after the fact.
