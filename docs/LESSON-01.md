# Lesson 01 — Give every cultivator an aperture (1.7)

**Where code goes:** `Source/ProjectGu` stores C#; `Defs/HediffDefs/GU_Aperture.xml` registers it in the game; `About/About.xml` tells RimWorld this is a 1.7 mod.

**How it works:** `GuWorldComponent : GameComponent` runs once each game tick but returns immediately except every 600 ticks. At that interval it checks humanlike pawns spawned on loaded maps. A pawn with no aperture receives `Hediff_Aperture`.

**The aperture itself:** `Hediff_Aperture : Hediff` holds aptitude (40–100), rank (1), and current essence. `AptitudeGrade` is a computed C# property: at least 90 = A, at least 70 = B, at least 50 = C, otherwise D. `MaxEssence` equals aptitude in our *prototype* rules. `Tick` replenishes essence in small increments.

**Persistence:** `ExposeData` writes and restores `guAptitude`, `guRank`, and `guEssence` using RimWorld's Scribe system. Keep these keys stable even if we rewrite the UI later.

**Your first exercise:** Change the label in `GU_Aperture.xml` to `primeval aperture`, rebuild/reinstall, and confirm the Health tab reflects the change. This teaches the XML-to-C# connection before you modify game logic.

**Second exercise:** Change the 600-tick initialization interval to 300 ticks and record whether you can notice the difference for newly spawned pawns. Never substitute frequent full-map scans for proper event-based triggers in a large simulation.

**Next milestone (0.2):** NPC cultivation goals and a bounded decision scheduler. We will keep pawn data here, put decision rules in a separate service, and add logs for decisions and consequences.
