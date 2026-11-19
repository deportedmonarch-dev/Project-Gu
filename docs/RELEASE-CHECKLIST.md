# Release checklist — Project GU

- [ ] Snapshot or commit current source and record working beta build.
- [ ] Install/confirm RimWorld **1.7** (not 1.6).
- [ ] `python tests/validate_scaffold.py` passes.
- [ ] `scripts/verify.ps1` compiles against installed 1.7 assemblies.
- [ ] In-game smoke tests pass without new red log errors.
- [ ] Test an older Project GU save (keep a backup), then a fresh game.
- [ ] `About.xml` still advertises **1.7 only**.
- [ ] Archive contains `1.7/Assemblies/ProjectGu.dll` and no game DLLs.
- [ ] Bump project version and write down tested game build.
