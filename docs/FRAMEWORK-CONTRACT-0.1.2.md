# Project GU Framework Contract — 0.1.2

## Design doctrine
* **One authority per fact**: Core owns aptitude, cultivation, institutional records and resource quantities. Integrations propose observations/actions, never duplicate canonical inventories.
* **Loaded and off-map are two views of one state**: no settlement can receive production from both a live map and an abstract simulator for the same interval. Transfer ownership atomically at activation/deactivation.
* **Bounded work, not endless polling**: event-driven when possible; for periodic work use staggered schedules, explicit budgets, and stable entity IDs. Avoid broad per-tick scanning or unconditional retries.
* **Fail visibly and safely**: log transition errors once with context; quarantine problematic actors instead of repeatedly retrying a failing activity every tick. Never silently delete player resources.
* **Adapters are optional**: no references from Core to Royalty, Biotech, Ideology, Anomaly, Odyssey, Rim War, Rimesis, Progression: Worlds/Scenarios, or Living Society. Add-on compatibility will be separate assemblies.
* **Deterministic time**: use game ticks and explicit last-processed ticks for off-map activity; cap catch-up work and record elapsed time rather than rerunning missed ticks one by one.
* **Persisted identity**: names/labels are not IDs; snapshots, transactions, and event histories require stable identifiers and migration handling.
* **No claim of perfect compatibility**: keep migration tests and backup saves across unstable 1.7 updates.

## 0.1.2 actual code
- `Domain/AptitudeRules.cs`: centralized thresholds and recovery values, no external dependencies.
- `Contracts/GuContracts.cs`: initial immutable identifiers, `GuEvent` message envelope, and two *prospective* integration interfaces. Not yet registered or used at runtime.
- Existing Hediff persistence keys are unchanged.
- Existing 600-tick spawned-pawn initialization scan is intentionally unchanged for 0.1.2 so this code-only refactor does not jeopardize the verified 0.1.1 behavior. Replace it with an incremental, event-supported initialisation system in a separately tested milestone.

## Anti-pathology checklist
- No two systems write the same resource balance.
- No per-tick log spam, missing-patch retry loops, or repeatedly failed pawn jobs.
- An off-map settlement is never processed twice for an elapsed interval.
- Unloading/loading does not create extra inventory, new inhabitants, or free training progress.
- Never use a disabled mod's classes before checking its presence.
- Every generated job must have an exit/cancellation route.
- Performance budgets and explicit instrumentation before large-scale faction simulation.
