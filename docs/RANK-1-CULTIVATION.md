# Rank 1 cultivation foundation

## In-game progression

Start **Southern Border Clan** and unpause. The elder starts at Rank 1 Initial. A disciple enters the same stage when awakened at the spirit spring. Inspect a cultivator's **Health → Aperture**, or open **Manage aperture Gu → Inspect aperture Gu**, to see the actual minor stage, raw volume at each quality, shared capacity and paid wall nourishment.

1. Select a conscious, undrafted cultivator and choose **Manage aperture Gu → Nourish aperture walls**. The cultivator works at their current location.
2. Each 250 work ticks, the aperture uses up to one raw unit, beginning with the highest stored quality. The work bar and aperture inspection show the paid nourishment toward the next stage. Stop or draft the cultivator to interrupt. Choose **Nourish aperture walls** again to continue. Paid progress and partial work survive interruption and save/load.
3. When the wall reaches its requirement, choose **Advance to Middle**, **Advance to Upper**, or **Advance to Peak**. Advancement uses the accumulated nourishment and leaves the stored essence mixture intact. New natural recovery is at the new stage's quality.
4. At Rank 1 Peak, the aperture continues recovering essence and powering Gu. The progression ends there.

| Actual stage | Next stage | Nourishment required | Raw essence at the current stage's quality | Minimum work at that quality |
| --- | --- | ---: | ---: | ---: |
| Initial | Middle | 24 | 24 | 2.4 in-game hours |
| Middle | Upper | 80 | 40 | 4 in-game hours |
| Upper | Peak | 224 | 56 | 5.6 in-game hours |
| Peak | — | — | — | — |

## Essence accounting

- One aperture owns all four quality balances. Their raw volumes share the existing aptitude-based cap. Awakening fills half the capacity with Initial essence. A/B/C/D recovery remains 8/6/4/2 raw units per 2,500 ticks and credits only the current stage's quality. Aptitude affects how long a cultivator can sustain work and how quickly they can resume. Every aptitude can reach Peak.
- Initial, Middle, Upper and Peak provide respectively 1, 2, 4 and 8 wall nourishment per raw unit spent. A pulse may blend qualities or spend less than one unit to reach the exact requirement. This is the prototype's mechanical advantage for higher-quality essence.
- Raw ability and refinement costs use all quality balances at face value, lowest quality first. Moonblade still costs 3. Phase 3B still uses each Gu's quoted will-enforcement cost and duration. Refinement calculates the next charge from cumulative work and prior paid essence. A failed synchronous ability launch restores the exact mixture debited for it.
- The current save stores each balance and paid nourishment in integer billionths, actual stage separately, and the number of ticks toward the next pulse. API amounts round to the nearest billionth. The final nourishment pulse rounds its raw requirement up to the smallest representable amount and caps the credited progress. An Alpha 2 scalar balance under `guEssence` loads as Initial-quality stock at Rank 1 Initial.

## Liquor Worm integration contract

The caller should validate the Gu's ownership, custody, food and use requirements, then commit through the exact `Hediff_Aperture`:

```csharp
RankOneStage expectedSource = aperture.Stage;
double available = aperture.EssenceOfQuality(expectedSource);
// On commitment, after the caller validates the action and amount:
bool converted = aperture.TryUpgradeEssence(expectedSource, amount);
```

`TryUpgradeEssence` is synchronous and all-or-nothing. It moves the requested positive raw volume from `expectedSource` into the immediately higher quality. The source must equal the current actual stage and have sufficient stock. Peak has no output. The method preserves total raw volume, actual stage, wall progress and aptitude. A second call using the improved quality as input fails until the player's actual stage reaches that quality. Callers should execute it once at their successful commitment point. For ordinary raw costs, use `CanSpendEssence(amount)` for a preview and `TrySpendEssence(amount)` for the debit.

## Balance to evaluate in play

The 24/80/224 wall requirements, 10 raw units per hour of continuous work, and 1/2/4/8 quality weights are provisional. Evaluate the number of sessions required at D-grade, how much capacity players want to reserve for combat, and how Liquor Worm feeding, recipe size and cooldown should limit access to higher-quality stock. Moonblade and refinement keep their existing costs at all qualities.

## Verification

```powershell
.\scripts\verify.ps1 -Focused
.\scripts\verify-priority0-live.ps1 -Profile RankOne,Alpha1,Alpha2,Slice3B3B
```

The RankOne profile starts a fresh Southern Border Clan, awakens a disciple, and advances the elder through the normal cultivation menu. It saves with a mixed essence stock and an active Middle-stage job, finishes progression after a process restart, and reloads the Peak aperture in a third process.

On RimWorld 1.7.5014 rev1195 with .NET SDK 10.0.401, the Release build passed with zero warnings or errors. Focused managed tests verified all four recovery rates, shared capacity, lossless conversion, mixed-quality spending and rollback, exact refinement installments, and a D-grade path to Peak.

The native headless RankOne profile passed all three stages with Core and Harmony. An aptitude-70 elder advanced through 24, 80 and 224 paid nourishment in one colony. Drafting preserved partial work. The Middle reload restored 111 partial ticks and exactly 28.4 Initial and 41.2 Middle essence. The Peak reload restored 28.4 Initial, 17 Middle, 0.9 Upper and 0.6 Peak. The three stages logged zero red errors. Local evidence is in `tests/obj/Priority0-fc1a0054eab1452193dc890d2eb7dbbd/RankOne/`.

The same campaign passed all eight regression stages across Alpha1, Alpha2 and Slice3B3B. Alpha1 covered awakening, interrupted 12-essence Hope Gu refinement and custody after reload. Alpha2 fired Moonblade with a mixed Initial/Middle payment of exactly 3 raw essence. It also verified free interruption before launch and one resolution after a fresh-process in-flight reload. Slice3B3B preserved two separate Gu identities in one aperture through reload, withdrawal and re-storage. Campaign evidence is in `tests/obj/Priority0-fc1a0054eab1452193dc890d2eb7dbbd/campaign-summary.txt`.
