# Prototype 0.2.0 — In-game verification (RimWorld 1.7)

**Back up your 0.1.2 save first.** Run Core + Project GU only. The test-awakening
command is a developer tool, not a production ritual.

1. BUILD: Run `scripts/build.ps1` then `scripts/install.ps1`; restart RimWorld.
2. NEW GAME: Crashlanded scenario. Wait 600 ticks. All new pawns should be mortals:
   **no** `cultivation aperture` condition in Health. No red errors.
3. DEV MODE: Settings -> Development mode. Open debug actions -> `Project GU`.
   Choose `Inspect hidden cultivation potential` and click a pawn; note the state
   `Unattempted` and hidden grade/none. Repeat: grade must be stable.
4. AWAKEN: Use `Test awakening on pawn`, select the same pawn. Successful candidates
   acquire one Health aperture and Rank 1; candidates with none fail and stay mortal.
   Second attempt must not duplicate an aperture or reroll aptitude.
5. PERSISTENCE: Save, reload, inspect the same pawn. State, grade, and essence should
   persist. Repeat attempt: awakened/failed result unchanged.
6. LEGACY MIGRATION: Load a **copy** of the old 0.1.2 save with Nana/Panama.
   They must retain their exact aptitude, existing aperture, rank, and essence.
   Dev inspect should report Awakened (after the 600-tick discovery).
7. NPC: Spawn a new humanlike pawn with RimWorld debug tools; after 600 ticks,
   no aperture. Inspect and test-awaken if desired.
8. OPTIONAL: Run at 3x for 2 game days; check red errors and major lag.

**Expected limitation:** All newly discovered pawns are mortal, including scenario
pawns that you may intend to start as Gu Masters. Scenario-aware starting Gu Masters
are a later milestone. Off-map pawns receive a record when first encountered by the
service or discovered on a loaded map; we're not globally initializing all world pawns.

**Known interim design:** map discovery still runs every 600 ticks, as in the
validated baseline. It no longer creates apertures automatically; no work runs per
pawn beyond an indexed check. We'll migrate to lifecycle notification later.
