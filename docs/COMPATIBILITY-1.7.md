# RimWorld 1.7 beta-to-stable compatibility policy

**Target 1.7 exclusively.** We are not supporting 1.6. We cannot guarantee Ludeon will never change a beta API; a guarantee would require control over their engine. We can design the project to minimize risk.

1. Reference the locally installed 1.7 game assemblies. Do not check proprietary game DLLs into git.
2. Use `net10.0` and emit only `1.7/Assemblies/ProjectGu.dll`. Never package a 1.6 build.
3. Avoid Harmony, reflection, private methods, Unity engine bindings, and external mod dependencies in prototype 0.1.
4. Use game-supported XML Defs, `GameComponent`, `Hediff`, and `Scribe_Values` for persistence.
5. Keep serialized keys (`guAptitude`, `guRank`, `guEssence`) and XML `defName` / `packageId` stable so old Project GU saves remain recognizable.
6. Before upgrading a 1.7 beta to a newer beta or 1.7 stable: back up the mod, branch/tag the code, archive a test save, build against the updated game DLLs, run static validation, then load old and new test saves.
7. Test at 1x/3x speed, spawn new pawns, move between maps, and verify no duplication and correct state after reload. Check error log.
8. If a beta engine API changes, edit code at one integration point; keep rules in self-contained classes rather than scattered engine patches.
9. Postpone Harmony until necessary. If introduced, explicitly target Harmony 3 and test every patch with the current 1.7 build.

## Version verification gates

- Build: `./scripts/verify.ps1` (PowerShell).
- In-game: `docs/TEST-PLAN.md`.
- Archive release notes: `docs/RELEASE-CHECKLIST.md`.
- Use an isolated test save and a lightweight mod list (Core + Project GU) when debugging.

Useful references:
- https://rimworldwiki.com/wiki/Modding_Tutorials/RimWorld_1.7_Mod_Updates
- https://rimworldwiki.com/wiki/Modding_Tutorials/Setting_up_a_solution
- https://ludeon.com/blog/2026/10/announcing-the-waylith-engine-and-update-1-7/
