# 0.2.2 in-game tests (Core + Project GU only)

1. Compile/install/restart. Verify **no red Project GU errors** at startup.
2. New Crashlanded test game: all three pawns initially lack an aperture.
3. Spawn one spirit spring through developer mode. Select an unawakened colonist; right-click the spring and select **Attempt awakening at spirit spring**.
4. Verify the pawn moves to the spring, waits about 600 ticks, and the awakening message appears only **after** finishing.
5. On success, a single Rank 1 aperture appears. On failure, pawn remains mortal and the failure state is saved. The spring's Hope Gu count decreases by one in both cases.
6. Retry with the same pawn: no additional activation, aperture, or resource consumption.
7. Interrupt a different pawn's job during the wait: no awakening and no Hope Gu consumption. Then restart it normally.
8. Save/reload: Hope Gu count, spring stone count/timer, pawn states, stones and apertures persist.
9. Three ceremonial attempts should drain the spring's initial Hope Gu capacity. A fourth eligible pawn should see a disabled menu. After one in-game day the spring should recover one charge.
10. Verify your previously awakened legacy save remains unchanged.
11. Verify spawned stones are selectable and haulable; no missing textures.

**Important:** This remains a prototype; it does not yet simulate the canonical 10–50 step Moon Orchid / Hope Gu procession, subterranean springs, scheduling, or clan-owned ceremonies.
