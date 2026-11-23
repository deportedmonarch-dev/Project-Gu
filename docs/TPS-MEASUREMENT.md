# TPS measurement (M0)

**Status** — Tooling implemented on `feature/m0-tps-harness`. Static checks, game-linked compilation and managed tests pass. **No native measurement has run yet.** Native validation is authorized only after the current Codex Phase 3A checkpoint.

M0 measures how many simulation ticks per second RimWorld 1.7 achieves with and without Project GU, from the same starting save, so later gameplay milestones can be held to a performance budget.

## Targets in engine terms

The 1.7 `TickManager` targets 60 ticks per second times the speed multiplier:

| Speed | Multiplier | Target TPS |
|---|---:|---:|
| Normal | 1 | 60 |
| Fast (second button, "3×") | 3 | 180 |
| Superfast (third button) | 6, or 12 when nothing is happening | 360 or 720 |
| Ultrafast (development) | 15 | 900 |

The owner targets of about 360 TPS and 760+ TPS therefore correspond to Superfast and to Smart Speed. A frame also runs at most 2 × multiplier ticks and stops after 45.45 ms. Throughput above the target is necessary but not sufficient: frame rate and rendering also limit achieved speed.

## Method

The test-only mod `projectgu.tpsprobe` (`tests/ProjectGu.TpsProbe`) has no Project GU reference, so it loads identically in both variants. It is staged only by the runner's `Tps` profile and does nothing without its `-gutps` switch. It adds no Harmony patch to the simulation. Its only patch is the existing headless map-dispose guard on exit.

1. **Controlled start.** The runner generates one vanilla quick-test colony with Harmony, Core and the probe (`prepare` stage), or copies a supplied save. Every measured process loads a fresh clone of that one file as `autostart.rws`. A supplied save is hash-checked after the run.
2. **Variants.** `Vanilla` loads Harmony, Core and the probe. `Gu` adds Project GU. Both use the same save, so the difference is the cost of Project GU on that colony. A Project GU save, for example one with a spring cavern, can run only as `-TpsVariants Gu`.
3. **Repeats.** Each variant runs in separate fresh processes (`-TpsRepeats`, default 3). The order alternates per repeat (V G, G V, V G) to spread thermal and cache drift. The run must be alone on the machine, like the registry benchmark.
4. **Uncapped mode** (default). Native pacing is paused, and the probe calls `TickManager.DoSingleTick` from the frame update in batches of up to 50 ms. It discards a warmup (`-TpsWarmupTicks`, default 2,500 = one in-game hour), then times every tick in `-TpsWindows` windows of `-TpsWindowTicks` (default 5 × 2,500). Each tick costs two `Stopwatch` timestamps.
   - *Simulation TPS* = ticks / summed tick time. It excludes engine frame work.
   - *Wall TPS* = ticks / window wall time. It includes the frames between batches.
   - Per-tick mean, p50, p95, p99 and maximum.
5. **Paced mode** (`-TpsModes Paced`). Native pacing runs at `-TpsSpeed` (default Superfast). After a warmup it records achieved ticks per wall-clock second, frames per second, ticks per frame, and the time-weighted native target. It also records forced-normal-speed frames (combat slowdown) and any pause, which is undone and counted. With `-Rendered` this measures a rendered window at vanilla pacing.
6. **Environment.** Automatic pausing is set to never, and dev mode is switched off after the autostart load needs it. Both changes go only to the run's own config folder. Engine file logging is off. The runner's power request and sleep detection apply as for every campaign.

**Recorded per process** (`tps-<stage>.json`):
- mods and game components loaded
- start and end tick, maps with size, spawned pawns and things
- world pawns, alive and dead
- factions
- managed heap, allocated bytes, gen 0/1/2 collections, working set and private memory
- per-window statistics and a summary with the spread across windows

**Rolled up per campaign** (`tps-summary.csv`, `tps-summary.txt`, campaign summary):
- per-process rows
- median, range and coefficient of variation across processes for each variant and mode
- the Project GU / vanilla ratio
- headroom against each vanilla target

## Usage

```powershell
# Uncapped comparison on a generated vanilla colony: 3 processes per variant.
scripts/verify-priority0-live.ps1 -Profile Tps

# A representative colony save, more repeats, plus paced Superfast.
scripts/verify-priority0-live.ps1 -Profile Tps -TpsSourceSave D:\saves\colony.rws -TpsRepeats 5 -TpsModes Uncapped,Paced

# A Project GU save (no vanilla counterpart), paced and rendered.
scripts/verify-priority0-live.ps1 -Profile Tps -TpsSourceSave D:\saves\clan.rws -TpsVariants Gu -TpsModes Paced -Rendered
```

`-TpsPrepareTicks N` advances the generated colony before saving it. The generated source stays in the run's evidence folder, and its SHA-256 is in the summary. Pass it back as `-TpsSourceSave` to compare later campaigns on exactly the same start. The stage timeout defaults to 900 s for this profile.

## Reading results

- Compare variants **within one campaign**, on one source save and one machine. Absolute TPS depends on the colony, the hardware, thermal state and background load.
- A ratio below 1 is Project GU's cost on that colony. Use the per-tick p99 and the gen 2 collections to see spikes and garbage-collection pressure, not just the mean.
- Uncapped simulation TPS bounds what native pacing can achieve. Paced mode shows what the native loop actually delivers at a speed.
- RimWorld's simulation is not bit-for-bit deterministic across processes: storyteller events, jobs and pathing vary. The spread across windows and processes is reported for that reason. Treat differences smaller than the coefficient of variation as noise.

## What M0 does not measure

- **Owner-rendered gameplay and Smart Speed.** Smart Speed is not staged by the runner. Owner protocol:
  - Load the same source save with the vanilla and the Project GU mod lists.
  - Let it run one in-game hour at the chosen speed.
  - Read the TPS display for 60 s, three times per variant, without camera movement.
  - Record hardware, speed setting and mod list.

  These are owner reports and are labeled separately from automated results.
- A generated quick-test colony is small: 3 colonists and one map. Representative numbers need representative saves: a mid-game colony, and a clan save with the spring cavern once M1 exists.
- No per-feature attribution. A difference shows that Project GU costs ticks, not which system. Profiling a specific system is follow-up work once a cost is found.

## Native validation still required

Authorized only after the current Codex checkpoint. Run alone on the machine.

1. **Smoke run.** `-Profile Tps -TpsRepeats 1 -TpsWindows 2`. Confirms: generation, autostart load of the clone in both variants, the probe's activation, both mod lists in the reports, report writing, clean exit, no isolation warnings and an unchanged shared user data folder.
2. **Repeatability.** The default campaign twice on the same generated source, the second time via `-TpsSourceSave`. Accept the harness when per-variant medians agree within the observed coefficient of variation, with a target of ±3%.
3. **Paced mode.** `-TpsModes Paced` at Superfast. Confirm achieved TPS is at or below the native target and that pauses are recorded as zero.
4. **Rendered paced run.** One `-Rendered -TpsModes Paced` campaign.
5. **Representative baseline.** A supplied mid-game vanilla save, recorded as the M0 baseline for the M1 comparison.
6. **Owner reference.** Your rendered Smart Speed measurement on the same save.
