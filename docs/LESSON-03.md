# Lesson 03 — State, identity and one-way transitions

Look at `GuPawnRecord.cs`. The record is not a Health condition. It stores the
**latent** potential of a pawn, even if that pawn never cultivates. `0` means
no naturally awakenable aperture; the ordinary grades span 20–99.

`GuWorldComponent` owns a list of records saved via RimWorld's Scribe API.
A dictionary provides constant-time lookups **during play**, but is rebuilt
from the list after loading; it is not serialized separately.

`GuAwakeningService` is the only place that opens an aperture. Its transition
is Unattempted -> Failed OR Unattempted -> Awakened. It never rerolls talent.
An old save with a `GU_Aperture` Hediff is treated as already awakened.

In `GuDebugActions.cs`, `DebugAction` registers developer-only commands.
They are for verification, not a substitute for the later Hope Gu ceremony.

**Try this yourself:** After checking the baseline, find the provisional A-grade
probability in `LatentAptitudeRules.Generate`. Explain why modifying it on an
existing save won't reroll anyone (and why that is desirable).
