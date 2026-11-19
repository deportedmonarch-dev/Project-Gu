# Prototype 0.1 manual in-game smoke tests — RimWorld 1.7

Use **Core + Project GU** and a disposable test colony (do not use a valued save). In Options, enable development mode.

- [ ] Game launches in **1.7** with Project GU enabled and no red errors in the log.
- [ ] After at least 600 game ticks, select a humanlike colonist, inspect Health and hover "cultivation aperture".
- [ ] Tooltip shows aptitude 40–100, grade A–D, Rank 1, and essence capacity equal to aptitude.
- [ ] With time running, primeval essence regenerates but never exceeds max.
- [ ] Save game. Reload. Pawn aptitude, grade, rank and essence remain consistent (essence may regenerate as ticks advance).
- [ ] After 1,200+ ticks, pawn still has **one** aperture, not duplicates.
- [ ] Spawn/recruit another humanlike pawn. Within 600 ticks it gains aperture.
- [ ] Repeat after updating RimWorld to another 1.7 build. Log the exact game build in `docs/BUILD-LOG.md`.

Note: this starter does not yet initialize off-map pawns, animals, or dead pawns, and it does not include Gu abilities, breakthroughs, or independent NPC decisions. The initialization pass scans spawned pawns every 600 ticks. At scale this will be replaced with an event-based or bounded system.

On RimWorld 1.7 check `%USERPROFILE%\AppData\LocalLow\Ludeon Studios\RimWorld by Ludeon Studios\Logs\Player.log`.
