# Cleaning & dust

**Assembly:** `Features.Modules` · **Namespaces:** `Paint`, `Features.Modules.CleaningModule.Dust` · **Source:** `Features/Modules/CleaningModule/**`

The bot paints into a mask texture as it moves; the dust shader reads that mask to
decide where dust remains. The mask is indexed by world XZ. Masks persist as blobs
in the save container ([`core.md`](core.md#1-fact-spine)).

## Types

| Type | File | Description |
|---|---|---|
| `PaintCanvas` | `Paint/PaintCanvas.cs` | `[ExecuteAlways]`. Owns one `RenderTexture` and the projection onto it. `Stamp(worldPos, yaw, color, brush)` blits one dab through `PaintingShader`. `Clear(color)`, `Bind(material, property)`, `Texture`, `event Changed` (fires when the RT object is replaced). `PaintRect` returns `(min.x, min.z, size.x, size.z)` of `surfaceRenderer.bounds`. `ReadRaw()` / `WriteRaw(byte[])` transfer raw mip-0 bytes in the texture's own format. |
| `Brush` | `Paint/Brush.cs` | `struct { Texture footprint; float halfSize; }`. `halfSize` is in metres. |
| `StrokeEmitter` | `Paint/StrokeEmitter.cs` | Converts a moving point into evenly spaced dabs. Spacing is `halfSize * 2 * spacingFraction`; leftover distance carries across frames. `Extend(worldPos, color, brush, canvas)`, `End()`. |
| `PlayerPainter` | `Paint/PlayerPainter.cs` | Raycasts down from its transform over `castStart`+`castDepth`, resolves a `PaintCanvas` with `GetComponentInParent`, extends the stroke. Serialized `surfaceMask`, `color`, `brush`, `emitter`, `cycleHue`, `hueSpeed`. |
| `MousePainter` | `Paint/MousePainter.cs` | Paints from a screen ray. Debug tool. |
| `DemoMover`, `SetColorButton` | `Paint/*` | `PaintDemo` scene only. |
| `DustRenderer` | `Dust/DustRenderer.cs` | `[ExecuteAlways]`, requires `MeshFilter`. Draws `shellCount` concentric shells of the host mesh in one `RenderMeshInstanced` call. Pushes `_ShellCount`, `_MaxHeight`, `_DustMask`, `_PaintRect` through a `MaterialPropertyBlock`. Recomputes world bounds each frame. |
| `CleanableSurface` | `Dust/CleanableSurface.cs` | Requires `PaintCanvas`. Serialized GUID `id` and canvas reference. Registers and restores on enable, unregisters on disable. `CaptureMask()` writes bytes to `WorldState`. `OnValidate` assigns and de-duplicates the id. |
| `CleanableRegistry` | `Dust/CleanableRegistry.cs` | Static `Dictionary<string, CleanableSurface>`. `Register`, `Unregister`, `TryGet`, `All`. Runtime only. |
| `PaintingShader` | `Paint/PaintingShader.shader` | `Hidden/Cleanbot/PaintingShader`. Full-screen blit. Maps texel to world XZ through `_PaintRect`, transforms into the brush frame by `_BrushYaw`, samples `_FootprintTex` alpha as coverage. |
| `DustShell` | `Dust/DustShell.shader` | `Cleanbot/DustShell`. Offsets each shell along the normal by `t * _MaxHeight`; hashes grit cells from world XZ scaled by `_CellCount`; discards below the strand height; uses `_DustMask.r` as alpha. |

## The projection contract

The mask is indexed by world XZ over `surfaceRenderer.bounds`, published as
`PaintCanvas.PaintRect`. Every reader derives its UV from that value.

```
PaintCanvas.PaintRect ─► _PaintRect ─► PaintingShader  world  = rect.xy + uv * rect.zw
                      └─► _PaintRect ─► DustShell      maskUV = (worldXZ - rect.xy) / rect.zw
```

1. Mesh UVs are not used to index the mask. `DustShell`'s `Attributes.uv` is
   declared and unused.
2. `_CellCount` multiplies world XZ, so it means grit cells per metre and is
   independent of surface size and object scale.
3. `PaintCanvas.surfaceRenderer` must be the renderer on the object `DustRenderer`
   draws.
4. The projection assumes a surface whose normal is +Y. Rotation about any other
   axis invalidates it.

## Mask persistence

### Save

```
Button.onClick → SaveCoordinator.Save()
  foreach CleanableRegistry.All.Values
      CleanableSurface.CaptureMask()
          canvas.ReadRaw()                       AsyncGPUReadback + WaitForCompletion
          WorldState.SetBlob(FactKeys.SurfaceMask(id), bytes)
  WorldState.Save()
```

### Load

```
GameFlow.Begin(Continue) → WorldState.Load()     blobs in memory before the scene loads
scene loads
  PaintCanvas.Awake()                            RT created and cleared
  CleanableSurface.OnEnable()
      CleanableRegistry.Register(id, this)
      RestoreMask(): WorldState.GetBlob(SurfaceMask(id)) → canvas.WriteRaw(bytes)
```

### Contracts

1. `id` is a GUID serialized into the scene, assigned in `OnValidate` through
   `EditorApplication.delayCall` and followed by `EditorUtility.SetDirty`.
   `OnValidate` also runs during asset import, when a scene-wide search is unreliable.
2. Duplicating a surface in the editor assigns the copy a new GUID.
3. Surfaces register on enable and unregister on disable, so the registry holds no
   dead entries.
4. `CleanableRegistry.Unregister` removes an entry only when the stored value is the
   caller. `OnDisable` order during scene unload is unspecified.
5. A duplicate id is refused and logged as an error. The first registrant keeps the id.
6. `CaptureMask` is public and called by `SaveCoordinator`. `RestoreMask` is private
   and called from `OnEnable`.
7. `WorldState.GetBlob` returning `null` is a valid state: no save, a new game, or a
   surface added since the last save. The surface remains at its clear colour.
8. `WriteRaw` compares `bytes.Length` against
   `GraphicsFormatUtility.ComputeMipmapSize` and returns with a warning on mismatch.
9. `WriteRaw` creates its staging `Texture2D` with `_rt.graphicsFormat`, so
   `Graphics.Blit` performs no colour-space conversion.
10. `WriteRaw` blits into the existing render target and does not raise `Changed`.
11. `ReadRaw` blocks on `WaitForCompletion()`.

## Contracts

1. Mask polarity: white is dusty, black is clean. A dust canvas has an opaque white
   `clearColor` and its painter writes black.
2. `DustShell` reads `_DustMask.r`. A painter writing colour other than greyscale
   produces a red-channel value unrelated to coverage.
3. `bindToSurface` must be off on a dust canvas. On, the mask render texture is also
   assigned to the surface material's `_BaseMap`. `Bind` uses `renderer.material`,
   which instantiates a material copy.
4. `PaintCanvas` creates its render texture with `useMipMap = false`.

## Authoring

A cleanable floor carries: `MeshFilter`, `MeshRenderer`, `MeshCollider`,
`PaintCanvas`, `DustRenderer`, `CleanableSurface`.

| Field | Component | Requirement |
|---|---|---|
| `surfaceRenderer` | `PaintCanvas` | the `MeshRenderer` on this object |
| `bindToSurface` | `PaintCanvas` | off |
| `clearColor` | `PaintCanvas` | opaque white |
| `paintShader` | `PaintCanvas` | `Hidden/Cleanbot/PaintingShader` |
| `maskCanvas` | `DustRenderer` | the `PaintCanvas` on this object |
| `dustMaterial` | `DustRenderer` | a material using `Cleanbot/DustShell` |
| `id` | `CleanableSurface` | assigned automatically; never edit by hand |

A painter requires `brush.halfSize` greater than zero and an assigned
`brush.footprint`; the defaults are zero and null. For dust, `color` must be black
and `cycleHue` off.

Restoring a mask requires entering play through the Title scene. Entering the
Gameplay scene directly leaves `GameSession.Consume()` at `None`, so
`WorldState.Load()` never runs.
