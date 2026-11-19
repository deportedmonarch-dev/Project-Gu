# Project GU 0.2.1 — Stone Hotfix 2

Fixes the installer omitting the `Textures` directory, changes the stone to a single-image graphic (the supplied package only includes `PrimevalStone.png`), and enables stack-count overlay for stackable items.

The original fix (`selectable` and `alwaysHaulable`) is retained. No save keys or cultivation mechanics were changed.

**Testing**: Build and install from the inner folder, completely restart RimWorld, spawn a **new** `GU_PrimevalStone` on open ground (not under the spring), select it, verify the item panel and hauling, then save/reload. Do not rely on a previously hidden item under a structure for the first test.

The historical spring counter is not a guarantee that the item remained accessible. No automatic migration or relocation of previously created stones is implemented.
