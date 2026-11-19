# Project GU — Aptitude Specification 0.1.1 (RimWorld 1.7)

## Confirmed / adapted lore
- D-grade: 20–39 percent sea capacity; C-grade: 40–59; B-grade: 60–79; A-grade: 80–99.
- Ordinary aptitude determines sea capacity, influences natural recovery and cultivation prospects; advancement is **not** hard-capped by grade.
- Early novel (around chapter 10): C-grade regenerates about 4 percentage points per in-game hour, A-grade about 8.
- Ten extreme physiques have 100% aperture capacity and special passive cultivation/instability; they are not just A+.

## Explicit GAME BALANCING choices, not established canon
- B-grade = 6 and D-grade = 2 essence units/hour (2500 RimWorld ticks). The 'essence units' assume a 100-unit full sea and one capacity point per aptitude point.
- A-grade = 8 and C-grade = 4 units/hour for the same unit convention.
- Physiques are **not generated or mechanically enabled** in this release; their ID is serialized to prevent future save schema changes, and they currently fall back to A-grade recovery if present.
- Population distribution remains an arbitrary temporary 40% D / 30% C / 20% B / 10% A **among spawned humanlike pawns**, not true Gu Master demographics.
- Prototype 0.1.1 still opens every humanlike pawn's aperture; version 0.2 will address ordinary mortals, awakening and eligibility.

## Critical design constraints for later
- Cultivation progress and natural recovery are distinct: nurturing the aperture walls must *spend* essence, rather than assuming recovery automatically increases rank.
- Primeval stones and Gu abilities should spend/restore real resources, enabling wealthy low-aptitude pawns to overcome some disadvantages.
- Grade should influence institutions and AI ambition, **not hard-code absolute progression limits**.
- Ten extreme physiques: separate type ID, 100% capacity, passive cultivation (at relevant rank), instability/rupture dangers, countermeasures. No rare random generation until awakening framework exists.
- Keep save keys stable: `guAptitude`, `guRank`, `guEssence`; new key `guExtremePhysiqueId` defaults empty.

## Test after upgrade
1. Back up a version 0.1 save first.
2. On your existing test colony, aptitude values and current essence should persist; grades may change due to corrected cutoffs (e.g., 40 goes from D to C).
3. Over ~2500 game ticks, a partly depleted A-grade should recover ~8 essence units; B 6; C 4; D 2 (unless full).
4. New pawns should generate grades in the correct ranges, with no automatic extreme physique.
5. Save/reload again; verify no duplicated Hediffs, errors or lost essence.

## Source guidance
- Early Reverend Insanity chapter 10 describes about 4%/hour for C grade, around 8%/hour for A grade.
- Northern Dark Ice Soul physique is discussed in chapters around 135 and 140.
- Exact percent-to-RimWorld-ticks mapping is a game-design decision.
