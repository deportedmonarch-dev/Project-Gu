# Live test infrastructure

`scripts/verify-priority0-live.ps1` runs the native RimWorld fixtures in disposable games. This page records how the runner isolates instances, what it measures and the evidence for its parallel mode. Owner approval of 2026-11-22 covered timing instrumentation, configurable fail-fast and a controlled feasibility prototype of parallel profiles. The shared starting colony, hardlinked game copies and cached migration results are out of scope.

## Usage

| Parameter | Default | Effect |
|---|---|---|
| `-Parallel N` | 1 | Runs up to N independent units at once through one scheduler. N=1 is sequential. |
| `-FailFast` | off | Development only. After the first failure no new process starts. Running processes finish and keep their evidence. Official campaigns leave it off. |
| `-StageTimeoutSeconds` | 180 | Kills a process that runs longer and records a timeout. |
| `-GameCopyMode` | `Shared` | `Shared` uses one read-only game copy. `PerWorker` gives each worker slot a writable copy. Use it only if the game must write to its folder. |
| `-CaptureFileLog` | off | Sequential only. Keeps the engine file log and copies it into each stage's evidence. |
| `-SampleIntervalMs` | 1000 | Resource sampling interval. |

A unit is one profile, one V1 source save or one historical save. Its stages are dependent: a failed stage marks the rest of its unit `SKIPPED` and other units continue. The runner exits 1 if any unit fails, is skipped or aborted, if the game copy changed, if isolation failed or if the system slept.

The benchmark `Slice3B5BPerf` refuses to run with another profile, with `-Parallel` above 1 or while any RimWorld process runs. Its processes get no timing patches and keep the engine file log, as before.

The M0 throughput profile `Tps` has the same exclusivity, runs without timing patches or the engine file log, and stages a separate test-only probe mod. See [TPS measurement](TPS-MEASUREMENT.md).

## Isolation and ownership

- **Run ownership.** Each run holds an exclusive handle on `run.lock` for its lifetime. The operating system releases it when the runner exits or is killed. Stale-run recovery removes another run's game copy only when that lock can be opened and no process runs from that run's folder. A lock held between fresh-process stages keeps the copy. Evidence beside a copy is never removed. Runs from before run locks are removed only after two hours without evidence writes.
- **Read-only shared copy.** Staging and the build finish before any game starts. The runner then adds an inherited deny rule for the current user to the game copy: write data, append, write attributes, write extended attributes, delete and delete child. It denies file creation, modification, deletion and rename while allowing reads and execution. Before launching games it attempts each mutation and refuses to continue unless all are denied and reading works (`game-readonly-check.txt`). `icacls` rights cannot express this rule because its `W` includes SYNCHRONIZE, which blocks reading.
- **Integrity hashes.** A SHA-256 manifest of every file and directory of the copy is taken before games start and compared afterwards (`game-manifest-0.txt`). A change fails the run.
- **Per-instance writable state.** Each unit has its own save data folder with config, saves, shader and texture caches, fixture JSON, results, timing and stdout/stderr. After the run the runner checks that every stage log names its own folder and that every timing file names its own stage and process.
- **Engine file log.** Waylith writes `Logs\Player.log` under the user's LocalLow folder with no per-instance option (`RimWorldWaylith.Bootstrap.ResolveLogDataRoot`). Concurrent instances would truncate and interleave one file. Test processes therefore run with the engine's `-NoFileLog` switch and the runner checks that the shared user data folder did not change. Earlier campaigns also kept only standard output: each run overwrote the user's own `Player.log`.
- **Build.** A machine-wide mutex serializes the shared verification build output between concurrent runs.
- **System sleep.** The runner holds a system-required power request for the run. It also reads the Kernel-Power log afterwards. A sleep or resume during the run marks it invalid. One N=2 attempt slept for idleness mid-run and produced false timeouts before this was added.

## Timing

Runner-side, per process: launch, OS process start, observed exit (within one 100 ms poll; `Process.ExitTime` reads as 1601 for these processes) and wall time. Game-side marks come from `tests/ProjectGu.LiveVerification/LiveStageTiming.cs` and are relative to the process start. They cover mod construction, startup completion (static constructors), world generation, new-game map generation, save loading, first playable frame, the test body, each save and the exit request. Marks stay in memory and are written once at the end. `timing.csv` has one row per process, `resources.csv` the samples, and `campaign.json` and `campaign-summary.txt` the totals.

N=1 breakdown of 26 processes across the 12 Gu fixtures:

| Component | Per process | Share of 600 s |
|---|---:|---:|
| Engine start to mod construction | 3.9 s | 18% |
| Mod construction to startup complete (Defs) | 6.8 s | 29% |
| World generation (fresh stages) | 6.0 s | 12% |
| Map generation (fresh stages) | 4.8 s | 10% |
| Save load (reload stages) | 4.7 s | 11% |
| Test bodies excluding saves | 0.2–0.7 s | 2% |
| Saves | 0.2–1.0 s | 3% |
| Exit after the request | 0.2 s | 1% |

Fixed run overhead is about 50 s: the copy takes 4–7 s, build 2 s, preflight 1–3 s, and the read-only setup and probe under 1 s. The first full read of the fresh copy for the hash manifest takes 29–38 s, which is consistent with on-access antivirus scanning: CNG SHA-256 of the same 1.11 GB takes 2.9 s when warm. The post-run hash took 3 s in most runs and 28 s once. `SHA256.Create()` in Windows PowerShell 5.1 is the managed implementation and was 8× slower. The runner uses CNG.

## Sequential and parallel comparison

Same 12 profiles (`Slice3B1` … `ManifestationProbe`, 26–27 processes). Windows 11 build 26200, Ryzen 9 9955HX, 32 logical processors, 32 GB, RimWorld 1.7.5014 rev1195. All runs used the read-only shared copy.

| Level | Run | Wall | Scheduler | Sum of process wall | Peak game memory | Peak system CPU | Min free memory | Units |
|---|---|---:|---:|---:|---:|---:|---:|---|
| N=1 | `273efc45` | 653 s | 602 s | 601 s | 1.1 GB | 64% | 12.6 GB | 11/12 ¹ |
| N=2 | `19dc32da` | 476 s | 422 s | 816 s | 1.9 GB | 47% | 13.2 GB | 11/12 ¹ |
| N=4 | `829c2681` | 282 s | 231 s | 819 s | 3.7 GB | 84% | 14.6 GB | 11/12 ² |
| N=4 | `334b8ac7` | 329 s | 247 s | 885 s | 3.8 GB | 74% | 13.0 GB | 12/12 ⁴ |
| N=6 | `1207bb61` | 226 s | 176 s | 881 s | 5.4 GB | 69% | 12.9 GB | 11/12 ³ |
| N=6 | `3226bfe1` | 199 s | 148 s | 748 s | 5.3 GB | 83% | 10.7 GB | 12/12 ⁴ |
| N=4 | `cbda4ca7` | 255 s | 204 s | 701 s | 3.9 GB | 69% | 13.7 GB | 12/12 ⁵ |

Every run reported zero game copy changes, zero isolation problems and no change to the shared user data folder from its own processes. Disk use stayed at one 1.11 GB copy, removed after the run, plus 148–199 MB of evidence per 12-profile campaign (about 12–17 MB per profile).

1. `Slice3B2` failed intermittently because its spawn helper did not clear incidental Hediffs from generated test Gu, so retirement was correctly refused. Reproduced at N=1, fixed as in the other fixtures, and passed in all later runs.
2. `Phase2Lifecycle` failed once when earlier drops filled every side of its actor. `SpawnGroundGu` now makes space first, as the other call sites do.
3. `V2EngineProbe` found seven projections after reload instead of six, and one lacked the manifestation keys. It was seen once, at N=6, and was not reproduced in later runs. The check now names each unmatched projection. One hypothesis is that native world-pawn GC does not finish before the save under heavy load. Unconfirmed.
4. Another RimWorld instance from a different project ran during these samples (about 8% CPU). It wrote the shared user data folder, which the runner detected and reported.
5. Final acceptance campaign with no other RimWorld process on the machine. The shared user data folder did not change, the game copy did not change, no isolation problem was found, the system did not sleep, and V2EngineProbe found the expected six projections.

The startup cost per process rose from 10.7 s at N=1 to 14–15 s at N=2 and then stayed flat to N=6. A single instance already uses up to 61% of the machine's CPU during startup, and the laptop processor runs lower all-core clocks under sustained load. The slowest process at any level took 43 s against the 180 s timeout.

**Recommendation (accepted by the owner, 2026-11-22).** Use `-Parallel 4` for development and campaign runs of independent profiles. It cut this set from 653 s to 255–329 s, with 12/12 on the final code and over 13 GB of free memory. The runner's own default stays `-Parallel 1`; changing it needs separate authorization, so pass `-Parallel 4` explicitly. N=6 saves a further 50–80 s but is the only level with an unexplained intermittent failure, so it is not recommended. Keep the benchmark sequential and alone.

## Benchmark on the new runner

`Slice3B5BPerf` run `a6ab7840` passed alone, with no other RimWorld process on the machine: seven fresh processes, matched 0/120 snapshots with the same 120 hosts and aperture Hediffs, zero physical Gu, and identical save files within each variant. The game copy did not change and no isolation problem was found. The runner reported the shared user data folder as changed. Only `Logs\Player.log` and `Player-prev.log` were written, because the benchmark keeps the engine file log as it always has.

Medians of three processes per variant, against the accepted Phase 2 baseline `75b5cdc3`:

| Measurement | Phase 2 baseline | `a6ab7840` |
|---|---:|---:|
| World things / empty save | 3,728 / 4.77 MB | 13,075 / 8.66 MB |
| Save size added by 120 records | 94,585 B | 94,702 B |
| Registry rebuild | 584 µs | 708 µs |
| Sweep (every 250 ticks) | 45.6 µs, 15,360 B | 54.8 µs, 16,296 B |
| Ordinary registry tick | 0.003 µs | 0.004 µs |
| Native save, 120 records / empty | 545 / 523 ms | 751 / 527 ms |
| `Game.LoadGame`, 120 records / empty | 4,555 / 4,223 ms | 5,924 / 4,491 ms |

**World seed limitation.** `-quicktest` generates a world from a random seed in every run. The benchmark's absolute save size, save time and load time therefore depend on the world, not only on the code. Earlier benchmark worlds held 3,728 to 41,016 things, with empty saves of 4.8 to 20.3 MB. The Phase 2 baseline happened to draw a sparse world. Only the 120-record minus empty differences, measured within one run, compare across runs, and even those can scale with the world. The per-record save cost matches the baseline within 117 B. Rebuild and sweep rose about 20%, which fits a larger world: an earlier run on a 41,016-thing world (`fc03d159`) measured 1,374 µs and 78.6 µs.

**Unresolved load-time difference.** In this run, loading with 120 records took 1.1–1.4 s longer than loading the empty variant (contained 5,575–5,938 ms, warm empty 4,111–4,491 ms), and saving took about 200 ms longer. The gap was 0.3 s and 22 ms on the Phase 2 baseline world. The Phase 1 run `0c111560` on a world of similar size (12,614 things) showed no gap. Every 120-record process was slow, including the last, so cold reads of the fresh copy do not explain it. A quick reading of the load path found no scan that obviously scales with records times world size. With three samples per variant and different worlds, the cause is unknown. Confirming it needs repeated runs on a fixed seed.

## Other verification

- **Fail-fast** (`41a14ba1`). A V1 migration unit with an unknown source failed. Its dependent resave was `SKIPPED`. Slice3B1's running `new` process finished with its evidence, its reload was not started, Slice3B3A never started, and the run exited 1. The source save hash was unchanged.
- **Ownership.** A second runner started while the first was running Slice3B5B. Both passed and neither removed the other's copy. A synthetic test of the recovery code kept a copy whose owner held its lock with no game running (a stage gap), and kept one whose owner was gone but whose child process still ran. It removed the copy of a dead owner, kept a recent legacy run, removed one idle for three hours, and never touched evidence. A runner killed mid-campaign left a read-only copy that the next run recovered.
- **Migration and historical saves** (`657838e7`, N=4). All seven V1 saves migrated with the Phase 2 classification of 17 bound, 12 unresolved and 12 quarantined among 41 records, and reloaded in fresh processes. Both historical copies loaded. Every source hash was unchanged and matched `SHA256SUMS`.
- **Benchmark.** All three isolation refusals were verified: another profile in the same run, `-Parallel` above 1, and another RimWorld process running. Run `a6ab7840` then passed alone (see above).
- `scripts/verify.ps1` passed: static checks, game-linked compilation and managed tests.

## Remaining concerns

- **Unresolved:** the V2EngineProbe seven-projection case at N=6. It was not reproduced and is not fixed. It needs a reproduction with the new diagnostic before N=6 is used.
- **Unresolved:** the benchmark load-time difference with 120 records on a larger world. It needs fixed-seed runs.
- Benchmark absolute times depend on the random quicktest world. Compare within-run differences, not absolute times across runs.
- The first read of a fresh game copy costs about 30 s, probably antivirus scanning. An exclusion for `tests/obj` would remove it, but that is a system security decision for the owner.
- The deny rule binds the current user's processes. Changing the folder's access rules would bypass it, which the game does not do. The hash manifest remains the backstop.
- With `-NoFileLog`, evidence contains standard output (warnings, errors and fixture lines) but not the engine's verbose file log. Use `-CaptureFileLog` sequentially to keep it.
- A crash report would go to the shared user data folder. The runner does not pass `-CrashHandler`, and no crash occurred.
