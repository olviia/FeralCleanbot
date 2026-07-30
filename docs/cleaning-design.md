# Cleaning feature — design

One sentence: **the bot paints a mask texture as it moves; the floor's shader reads
that mask; how much of the mask is white decides how much dust was gained.**

Everything else follows from that.

*Design intent. For what is actually in the code, see
[`architecture/cleaning.md`](architecture/cleaning.md); for the shader's visual
reasoning, `dust-shader.md`.*

---

fix
a) Blobs never leave memory. Data.blobs holds every mask for the process lifetime. After a     
load, each mask exists twice — 4 MB on the managed heap and 4 MB in VRAM — and nothing frees   
the copy. It's dead weight from the moment RestoreMask uploads it until the next save
overwrites it. At one surface that's invisible; at twenty it's 80 MB of managed arrays doing   
nothing. Fix is about five lines: drop the entry once RestoreMask has consumed it. Safe,       
because CaptureMask re-reads from the GPU anyway. I'd do this one now — it's cheap and it's the
kind of cost that stays invisible right up until it isn't.

b) A version bump silently eats the player's save. Load() returns false, GameFlow.Begin logs a
warning and falls back to NewGame. Meanwhile SaveExists still returns true, so the Load button
is enabled. The player clicks Continue and gets a new game with no explanation. That's a       
product bug, not an architecture one, but it's latent right now.

c) Every surface is captured every save, including floors nobody has touched — a full readback
and deflate for an unchanged mask. A dirty flag set in PaintCanvas.Stamp would skip them.      
Simple, and it's the single best scaling win available.

d) No save slots. One hardcoded folder. Adding slots parameterises the path properties —       
contained, but it's a known future change nothing accounts for.


## Pieces

Three components and one shader. Nothing in Core except two small contracts.

```
Bot (ActorHost)
 └─ CleaningModule            Features/Cleaning
      raycasts down, paints, credits dust

Floor (any mesh — Plane, ProBuilder, imported)
 ├─ MeshCollider              what the ray hits
 ├─ MeshRenderer + shader     samples _DirtMask, draws clean vs dirty
 └─ DirtSurface               Features/Cleaning
      owns the mask RT, paints into it, knows its own clean fraction,
      re-accumulates dust over time, raises the threshold edge

Core additions (only these two)
 ├─ Tag.InventoryFull         added to the existing Tag enum
 └─ ICollector                Core/Player — mirrors IChargeable
```

**As built, `DirtSurface` split into three** — see *Decisions locked → Decomposition*.

| here | in code |
|---|---|
| `DirtSurface` (mask storage + painting) | `PaintCanvas` |
| `DirtSurface` (drawing) | `DustRenderer` |
| `DirtSurface` (identity, persistence, fraction) | `CleanableSurface` |
| `_DirtMask` | `_DustMask` |
| `CleaningModule` | not built — `PlayerPainter` is the standing spike |

## Flow

```
each frame   CleaningModule: if actor has any blockedBy tag → do nothing
                             raycast down → GetComponentInParent<DirtSurface>()
                             paint a swept segment from last position to current

~4× / sec    DirtSurface:    generate mips on mask, async-read the 1×1 mip
                             that average IS the clean fraction
                             crossed the threshold? → raise SO channel event

~4× / sec    CleaningModule: Δ = surface.CleanFraction − remembered
                             dust = Δ × surface.Area × dustPerSquareMetre
                             actor.GetModule<ICollector>().TryCollect(...)

each frame   UI:             polls the number, damps toward it (the climb is the juice)
```

No events fire during normal cleaning. UI polls, quests listen for edges only.

---

## Decisions locked

**Representation**
- The mask is the only source of truth. Anything derivable from it is never saved.
  *(Held under challenge 2026-07-30. The alternative — also storing the derived
  fraction so quest progress survives a lost blob — was rejected in favour of one
  rule with no special cases. Accepted consequence: a corrupt or missing mask blob
  loses that surface's progress, since derivation will overwrite the cached number
  with zero.)*
- World-space XZ projection, not UV2. Mesh UVs are ignored, so any mesh works.
- Resolution authored as **texels per metre**, computed from bounds, power-of-two,
  clamped for memory. Designer gets a quality multiplier.

**The projection contract** *(added 2026-07-30, after it broke)*
- The mask is indexed by **world XZ over `surfaceRenderer.bounds`**, published as
  `PaintCanvas.PaintRect`. Every reader derives its UV from that one value.
- This existed only as an unwritten assumption shared by two shaders. The dust shader
  sampled with mesh UV0, which agrees with world XZ only for an axis-aligned 0..1-UV
  quad — true in `PaintDemo`, false on the `Plane` in `Gameplay`, where the trail
  landed nowhere near the bot. **A contract in two heads is not a contract.**
- Consequence kept: grit density is per metre (`_CellCount` multiplies world XZ), so
  surface size and non-uniform scale no longer distort it.

**Decomposition** *(added 2026-07-30)*
- `DirtSurface` as designed held three secrets that change for different reasons, so
  it is three components: **storage** (`PaintCanvas`), **drawing** (`DustRenderer`),
  **identity + persistence** (`CleanableSurface`). Swapping shells for decals touches
  only the renderer; changing mask format touches only the canvas.
- Components are named for the responsibility they own, not the object they sit on.

**Persistence** *(added 2026-07-30)*
- Masks are **opaque blobs** in the save container, keyed `surface.{id}.mask`.
  `WorldState` never learns a blob is a texture; `PaintCanvas` never learns its bytes
  go into a save.
- Identity is a **GUID serialized into the scene**, self-assigned and collision-checked
  in `OnValidate`. Not name, not path, not instance id.
- Surfaces register on enable and unregister on disable, so the registry cannot go stale
  and needs no scene-change hook.
- **Restore is self-service, capture is orchestrated.** A surface restores itself in
  `OnEnable` because nothing beats that for load-order robustness; capture must complete
  for every surface before the file write, so `SaveCoordinator` drives it.
- **Object-scoped state restores itself on enable; scene-scoped state is placed by the
  bootstrap.**
- Binary, deflated, one sidecar for the whole save — **not PNG**, superseding the
  original build-order step 6. PNG encoding costs CPU and buys nothing for a mask.
  Measured: 1024² ARGB32 = 4 194 304 B raw → **21 406 B** on disk (~196:1). Storage is a
  non-issue; VRAM and readback time are the real costs.

**Painting**
- One **swept segment** per frame, last position → current. Not N stamps.
  One draw call, gap-free at any speed, no spacing constant to tune.
  *(As built: `StrokeEmitter` still emits N spaced stamps. The swept segment is
  outstanding.)*
- Standing still degenerates to a circle, which is correct.
- Mask polarity: **white is dusty, black is clean.** The painter writes black.

**Accounting**
- Clean fraction = average of the mask = its smallest mip. One 1×1 async readback.
- `dust = Δ fraction × area × dustPerSquareMetre`. Cleaning clean floor yields zero,
  so it is self-correcting with no tuning constants.
- Clamp negatives — re-accumulation must never pay out.
- Refresh the remembered fraction on first contact, or stale values pay wrongly.
- Stored as **integer hundredths** (4237 = 42.37). Exact arithmetic, exact save
  round-trip, fits the existing `WorldState.counters`. Displayed as two decimals.
- The two readbacks have very different costs and must not be conflated: the fraction
  reads **one texel** from the smallest mip (cheap even synchronously), the save-time
  capture reads the **whole buffer** (wants async).

**Thresholds**
- At 8/10 a surface is declared clean and its mask wipes to full white.
- The wipe is **animated (~0.5s), never an instant pop.**
- It pays out the remaining 20% as a completion bonus. Deliberate.

**Boundaries**
- Cleaning is one feature. `CleaningModule` and `DirtSurface` talk directly.
- Cleaning never writes quest state. It publishes two things: a live clean fraction
  (polled) and a threshold crossing (SO channel, fires both directions).
- Blocking is by **tag mask**, serialized on the module — GAS Activation Blocked Tags.
  Inventory sets `InventoryFull`; cleaning never learns inventory exists.
- Dust transfer is via `ICollector` on the actor, item-agnostic. Core never says "dust".
- **Blobs never raise `FactChanged`.** Facts drive logic; blobs drive pixels.
- **A static needs all three:** cross-scene lifetime, single instance, no per-instance
  config. `CleanableRegistry` passes; the next candidate may not.

**Scope**
- No rooms. Only surfaces that accumulate dirt. Quests target surfaces directly.
- No aggregator.

---

## Deliberately deferred

Not forgotten — waiting for a second case to teach us the right shape.

- `IDirtSurface` interface — one implementation exists, so a concrete reference is fine.
  Cheap to extract later because it never crosses a feature boundary.
- `SubstanceSO` strategy hierarchy — comes back when water is real.
- Per-object UV2 masks (`DirtSkin`) — try a per-prop scalar dissolve first; it is
  ~80% as convincing for ~5% of the pipeline cost.
- Dirt patches as discrete interactables — a different verb, stays a separate mechanism.
- ~~Assembly definitions to enforce layering at compile time.~~ **Done** — nine asmdefs;
  every Feature references only `Core`, and `App` is the sole assembly allowed to
  reference a Feature.
- A `SaveIdentity` component — the GUID lives on `CleanableSurface` until a second thing
  needs one. **Caveat:** identity is the exception to the wait-for-a-second-case rule,
  because ids in shipped saves cannot be reshaped. The window is open only until real
  players have save files.
- Async capture — `ReadRaw` uses `WaitForCompletion()`. Drop the wait and yield when the
  stall becomes visible; the change reaches two files.
- A read-back **budget** in `CleanableRegistry` (round-robin, N per frame) — needed at
  ~twenty surfaces, not at one.

---

## Build order

1. ~~**DirtSurface + shader.**~~ **Done.** Trail paints, shells read the mask.
2. **CleaningModule.** Raycast down, paint the swept segment. Still no dust.
   *Partially standing in: `PlayerPainter` paints but bypasses the module system,
   so it cannot be tag-blocked and cannot reach `ICollector`.*
3. **Clean fraction.** Mip readback, threshold event. ← **next**
4. **Dust accounting + UI.** Deltas, `ICollector`, damped readout.
5. **Re-accumulation over time.**
6. ~~**Save: folders + atomic rename, masks as PNG.**~~ **Done 2026-07-30**, as binary
   blobs rather than PNG. Built out of order because the save seam was needed early;
   this is the one deviation from "start at 1 and go in order."

---

## The save architecture, as built

The layering that the persistence work settled on. Written down because the
alternatives were argued and rejected, not merely unconsidered.

```
App/        knows Core + Features.  composition and cross-feature sequencing.
Features/   knows Core.  one feature never reaches into another.
Core/       knows nothing above it.
```

```
Core/SaveSystem/
  SaveData.cs        [JsonIgnore] blobs;  CurrentVersion is THE format version
  WorldState.cs      GetBlob/SetBlob;  Save/Load span both files;  version gate
  BlobFile.cs        internal. binary container layout. no version of its own
  FactKeys.cs        SurfaceMask(id)

Features/Modules/CleaningModule/Dust/
  CleanableSurface.cs    id, registration, capture/restore
  CleanableRegistry.cs   static. id → surface

App/
  SaveCoordinator.cs     the save sequence: capture masks, then commit
```

**Rejected: an `ISaveable`-per-object interface.** `object CaptureState()` has the wrong
*temporal shape* — GPU readback is request-then-wait, the signature is call-and-return —
so every implementation touching a `RenderTexture` is forced into a synchronous stall. It
also hands the encoding decision to the serializer's generic `object` handling, base64ing
megabytes into the manifest.

**Rejected: a live texture reference inside `WorldState`.** Not because of copying — a
reference costs nothing — but because `WorldState` is static and outlives scene loads
while `RenderTexture`s do not, and because holding a concrete Feature type in Core makes
the assembly graph cyclic. "No interface" and "reference in Core" are the same decision:
the interface exists precisely to let Core hold the reference.

**Rejected: the button owning the save sequence.** UI raises intent; `SaveCoordinator`
decides what a save consists of. Its body is the one place where a *missing* piece of the
save is visible — which is how the absent player-position capture stays obvious instead
of being nobody's job.

Precedent: PowerWash Simulator (Unity, same genre) ships exactly this shape —
`Player.sav` for progress, `<level>_WASHMAPS.sav` for all of a level's masks in one
binary, `.old` siblings for durability, no image files anywhere.
