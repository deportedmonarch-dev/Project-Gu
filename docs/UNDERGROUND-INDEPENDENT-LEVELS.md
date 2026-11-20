# Priority 0 underground lifecycle

## Independent engine addresses

The experimental provider uses one lazily registered `GU_UndergroundAddresses` planet layer. The layer creates a small native address grid at subdivision 1 and runs the native tile population step. It has no connections, world generation steps, world drawing layers or caravan navigation. Its addresses carry engine identity. Surface geography and passage destinations belong to site records.

RimWorld saves the layer in its world grid. An independent parent uses `GU_UndergroundLevel` and saves its level ID and generator Def. The native map saves its parent cross-reference. Project GU installs a Harmony prefix on `WorldGrid.RemovePlanetLayer` before the engine removes the layer registration and destroys its world objects. The layer is retained for the life of the world.

Windows RimWorld 1.7.5014 rev1195 and .NET SDK 10.0.401 passed the isolated managed registration and Scribe fixture. Three distinct addresses on the 32-tile layer resolved to three distinct parents and map bindings through native lookup. The isolated fixture restores the layer, parent metadata and map references. Full game map generation, save/reload, teardown and local travel are a separate live gate.

The lifecycle implementation uses the installed RimWorld 1.7 Harmony mod. The build detects the Steam Workshop assembly in the game's library. For a different game or library layout, pass `-HarmonyPath` to `scripts/verify.ps1` or `-p:HarmonyPath` to MSBuild. Harmony and the game assemblies remain external to Project GU.
