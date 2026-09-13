# Catalog health — standing warnings from the bake-test suite

The Bake Tests (`Tools ▸ HAF ▸ Bake Tests…`) surface per-model conditions that don't fail a bake but DO cost
something in game. This page is the standing list, so a warning scrolled past in a console isn't the only
record. Re-run the suite after fixing one and update its row; entries carry the date they were last confirmed.

**How to read it**: the engine draws at most **16,320 quads per draw fragment** and clips the overflow
*silently* — an over-ceiling model is missing its last-baked geometry in game right now, with no error
anywhere. (Full mechanics: HAF `docs/Vehicle-Lab-Quickstart.md` §9 and `docs/Vertex-Budget.md`.)

## Open warnings (confirmed 2026-09-13, full suite: 56 passed / 0 failed / 2 skipped)

| Model | Warning | In-game effect | Fix |
|---|---|---|---|
| **NuclearWarheads** (static) | `_ModelMesh` 16,897 quads — **577 over** the ceiling | last ~577 quads silently not drawn | tick **Multi-fragment split** in the Factory and re-bake (2 fragments), or shave ~1,200 tris via `Reduce to ~tris` |
| **AntiTankIFV** (animated) | track mesh `Bredley_CaterpillarAnimL` 17,613 quads — **1,293 over** | track tail geometry silently not drawn | animated bakes can't split — reduce the track in the Vehicle Lab (tread cells / dials) and re-bake |
| **SiegeHowitzersCar** | source file missing: `D:/Downloads/m114_howitzer_in_action.glb` | baked assets still work, but the model can never be re-baked, and the golden-snapshot / catalog test rows skip it | restore the file (or repoint `modelFile` at a kept copy under `D:/3DModels/`) |

*(`Retex_Era6_Common_StealthCorvettes_01` also reports SKIP in the whole-catalog row — by design: a
texture-only override has nothing to bake.)*

## Closed

| Model | Was | Closed |
|---|---|---|
| Abominations, DroneSquadFPV | glbconv refused their out-of-spec GLBs at load (`AnimationSampler byteStride`) — extraction failed, invariant rows red | 2026-09-13 — glbconv loads leniently (`ValidationMode.TryFix`); both models pass the full suite |
