# Project GU: Living Cultivation

An experimental *Reverend Insanity*-inspired Gu cultivation framework for **RimWorld 1.7 (Waylith / .NET 10)**.

## Current validated baseline: Prototype 0.2.4

The 0.2.4 **compile-hotfix** source is the stable prototype baseline. The project maintainer reported **10/10 in-game tests passing** on this version. The development branch adds 0.2.5 species definitions and spring cohorts. Its current game-linked build remains pending and requires the RimWorld 1.7 assemblies.

Working prototype systems include:
- Mortal/unawakened/failed statuses, hidden aptitude and persistent Gu Master apertures
- Awakening jobs and Hope Gu consumption
- Spirit springs with finite reserves and physical, haulable primeval stones
- Spirit flora support, mature/juvenile Hope Gu populations and ecological capacity
- Save migration and development testing controls

**Known limitations:** Roaming physical Gu and underground pocket maps are proposed. Spontaneous flora propagation, regional simulation, and an Ideology ritual adapter are future directions. Current test parameters are provisional and not always canon-calibrated.

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
2. Review substantial architecture decisions and obtain owner approval for gameplay scope before implementing.
3. For each feature: verify/build, install, test in-game, check save/load and regressions, fix bugs before advancing.
4. Preserve `About/packageId`, Def identifiers, serialized Scribe keys, and save migration behavior.
5. Keep Gu ecology authoritative and avoid duplicating physical Gu during population ↔ instance conversions.

See [engineering instructions](AGENTS.md), [architecture](ARCHITECTURE.md), [project vision](docs/PROJECT-VISION.md), and the [roadmap](docs/ROADMAP.md) for current direction and verification status.

## Legal / attribution

This repository snapshot contains its own project source and art. **No license for Project GU has been selected in this preparation**. Until a license is selected by the owner, do not assume permission to redistribute or incorporate its source into other projects. Third-party material (including Deep And Deeper and Anima Expansion) is not bundled in this repository.
