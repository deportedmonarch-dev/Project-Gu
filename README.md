# Project GU: Living Cultivation

An experimental *Reverend Insanity*-inspired Gu cultivation framework for **RimWorld 1.7 (Waylith / .NET 10)**.

## Current validated baseline: Prototype 0.2.4

The 0.2.4 **compile-hotfix** source is the latest player-tested baseline. The project maintainer reported **10/10 in-game tests passing** on this version. This repository's initial state is intended as a stable starting point for the **0.2.5 Gu Framework architecture review**, not a public 1.0 release. This source snapshot has not been compiled in the repository-preparation environment; the Windows RimWorld 1.7 game assemblies are required.

Working prototype systems include:
- Mortal/unawakened/failed statuses, hidden aptitude and persistent Gu Master apertures
- Awakening jobs and Hope Gu consumption
- Spirit springs with finite reserves and physical, haulable primeval stones
- Spirit flora support, mature/juvenile Hope Gu populations and ecological capacity
- Save migration and development testing controls

**Known limitations:** No universal Gu species framework yet; no roaming physical Gu, spontaneous flora propagation, underground pocket-map integration, fully implemented region rules, or Ideology ritual adapter. Current test parameters are provisional and not always canon-calibrated.

## Requirements

- Windows RimWorld **1.7** installation, with `Assembly-CSharp.dll`, `Waylith.dll`, `UnityStubs.dll` in the game root
- **.NET SDK 10**
- Windows PowerShell

## Build and install

Run from the repository root in PowerShell:

```powershell
Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass
.\scripts\verify.ps1
.\scripts\install.ps1
```

`verify.ps1` runs PowerShell structural checks then builds. Only install after a successful build. Build outputs in `1.7/Assemblies/` are **intentionally excluded from Git**. Do not commit game binaries, SDK files, compiled DLLs, game logs, saves, or private data.

For focused gameplay testing, enable **Core + Project GU** and follow `docs/RELEASE-0.2.4.md`, plus earlier release test plans.

## Development workflow

1. Use the current passing build as the reference baseline; commit changes on a dedicated branch.
2. For **0.2.5**, write and review a Gu Framework RFC **before implementing**.
3. For each feature: verify/build, install, test in-game, check save/load and regressions, fix bugs before advancing.
4. Preserve `About/packageId`, Def identifiers, serialized Scribe keys, and save migration behavior.
5. Keep Gu ecology authoritative and avoid duplicating physical Gu during population ↔ instance conversions.

See `AGENTS.md` for repository-agent guidance and `docs/` for existing specifications and test plans.

## Legal / attribution

This repository snapshot contains its own project source and art. **No license for Project GU has been selected in this preparation**. Until a license is selected by the owner, do not assume permission to redistribute or incorporate its source into other projects. Third-party material (including Deep And Deeper and Anima Expansion) is not bundled in this repository.
