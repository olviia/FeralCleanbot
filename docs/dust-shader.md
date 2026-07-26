# Dust shader — design & plan

One sentence: **dust is drawn as N concentric shells of the host mesh in one
instanced draw, composited over whatever material is underneath by coverage —
so the base material never knows dust exists.**

Everything else follows from that.

**Visual target (interactive):** https://claude.ai/code/artifact/e918dc25-2898-40dc-8853-6053f85fc43c
Drag across the plate to wipe. Shows each constituent of dust isolated, then
stacked, plus the layer→technique mapping table.

---

## The reference reading

`docs/references/dust/` holds two photos. They are **two different physical
regimes**, and that distinction drives the whole design.

**The wooden table** — *film dust*, sub-millimetre. It has no height at all.
What makes it read as dust is entirely subtractive: the wood loses gloss, loses
saturation, and its blacks lift. Shells at that thickness produce only noise.

**The dusty glasses** — *lint*, a fibrous mat millimetres deep. This is what
shells are for. But the thing that sells it is that the rim is nearly **white**:
light passes *through* the fluff. It doesn't look fluffy because there are many
strands. It looks fluffy because it is translucent.

---

## The composition rule

Dust never touches the base material. It is a separate render of the same mesh:

```
final = (1 - α)·shade_base  +  α·shade_dust
```

That is plain alpha blending, and it is the physically correct model — a dust
film is not a *change* to the wood, it is a *partial covering*. Some fraction α
of each square millimetre is occluded by particles; the rest is bare surface.

What falls out for free:

| Base material | α = 0.6 | Why it's correct |
|---|---|---|
| Glossy metal floor | specular × 0.4 | gloss dies in proportion to coverage |
| Matte carpet | barely changes | carpet and dust already shade alike |
| Carpet's own shell shader | unaware it's dusted | no edge between the modules |

One rule, every base material behaves correctly, zero edits outside the dust
module. Second consequence: **the film is just shell 0.** One shader, one draw,
shell index picks behaviour.

### Options rejected

- **Uber-shader** (dust code in every material) — every material recompiles when
  dust changes, and you can't dust an asset you don't own. Fails Parnas at the
  first change.
- **DBuffer decals** (Battlefield / Doom / TLOU2 for world grime; URP has a Decal
  Renderer Feature) — genuinely right for *"this whole room is dusty"*, projected
  in world space. Wrong here: our mask is per-object and lives in UV space,
  because `PaintCanvas` already puts it there.

---

## Dependency graph

```
PaintCanvas  (owns the mask RT)            Features/Modules/CleaningModule/Paint
     │ .Texture / .Changed
     ▼
DustSurface  (per-object: shell count, height)   .../Dust/DustSurface.cs
     │ RenderParams
     ▼
DustShell.shader   shell 0 = film · 1..N = grit, mat, fibre

Base material ──── no edge to any of the above
```

Boundary check: the material owns **appearance** (colour, density, height); the
component owns **structure** (how many shells actually get drawn). `_ShellCount`
deliberately is *not* a shader Property — a material slider would be a second
source of truth fighting the component.

---

## Where we left off — 2026-07-25

**Step 0 is done. Dust renders correctly at runtime.**

`DustSurface.cs`
- One `Graphics.RenderMeshInstanced` call, N instances, shell index from the GPU
- Conservative world bounds (sphere-derived cube, rotation-proof, grown by
  `maxHeight × lossyScale`)
- Cached `Matrix4x4[]`, reallocated only when `shellCount` changes
- Mask bound through a `MaterialPropertyBlock`, `maskCanvas` optional

`DustShell.shader`
- Transparent queue, `Blend SrcAlpha OneMinusSrcAlpha`, `ZWrite Off`, `ZTest LEqual`
- `#pragma multi_compile_instancing`, `t` computed once in vert and interpolated
- URP main light + `SampleSH` ambient, `ao = lerp(0.4, 1.0, t)`
- Uniform strand cones from a single hash grid — **the thing Layer 1–3 replace**

### Decided, no code change needed

Mask polarity: the canvas clears to **white** (dusty), the brush paints **black**
(clean), and the shader reads `.r` directly as coverage. `PaintingShader` already
uses `Blend SrcAlpha OneMinusSrcAlpha` — source-over, not additive — so painting
black genuinely writes black. No paint-side changes.

### Deliberately deferred

`PaintCanvas` has no edit-mode lifecycle, so the mask can't be reset from the
Inspector. Not important; revisit if edit-mode iteration gets painful. What it
would need:

- `[ExecuteAlways]` on the class
- `Awake` → `OnEnable`, with `.material` → `.sharedMaterial` when not playing
  (`.material` instantiates a leaked copy on every domain reload)
- split creation from reset: a private `ApplySource()` plus a public
  `ResetToSource()`, so restoring the start state isn't welded to `EnsureCreated`
- a serialized `sourceTexture` — an authored dust distribution to reset *to*

---

## Remaining layers

Ordered by look-per-effort, not by render order.

### Layer 0 — Film (shell 0)
Shell 0 renders as a matte coverage layer with no strands. Alpha comes from the
mask. Kills the base's gloss for free via `(1-α)` — no touching base roughness.
Solves the table photo on its own.

Currently shell 0 is a **solid sheet**: at `t = 0` the test `0 > strand` is never
true, so it never discards. That's the film, just unmodulated.

### Layer 1 — Clump field (shared)
Multi-octave value noise in UV space, contrast-shaped by a clumpiness parameter.
Consumed by film alpha, grit density **and** shell height.

This is the single biggest fix over the current `Hash(cell) * falloff` grid — real
dust clumps at three scales at once (patches → tufts → fibres). Uniform dust reads
as carpet.

Note the interaction with the deferred `sourceTexture` work: procedural clumping
for high-frequency detail, an authored map for art-directed placement (heavy under
furniture, thin in walkways). Probably both.

**Also fix the non-uniform scale stretch here** — see Known issues. This layer
replaces the pattern generation wholesale, so the coordinate-space decision
(scale-corrected UV vs object-space position vs triplanar) is part of this work,
not a patch on top of it.

### Layer 2 — Grit
Two speckle populations — pale majority, dark minority — plus sparse larger
flakes, all modulated by the clump field. Slight normal perturbation so grit
catches light. Still zero added geometry.

The dark flecks matter more than the pale ones. They're what makes it read as
dust rather than fog.

### Layer 3 — Mat & fibre (rebuilt shells)
Replace the uniform cones:
- height becomes a **field** sampled from the clump noise, not a constant
- strands taper toward the tip
- per-clump direction field → fibres inside a tuft share a direction (matting,
  not fur)
- tangential lean under gravity — dust bunnies lie down, they don't stand up
- **dithered alpha / alpha-to-coverage** replacing the hard `discard`

That last one is also the escape hatch for overdraw: 24 blended layers with
`ZWrite Off` is 24× fill. Alpha-to-coverage with `ZWrite On` makes strands
order-independent quasi-opaque geometry.

### Layer 4 — Scatter
Wrapped diffuse plus a forward-scatter lobe so light passes *through* the mat,
plus Kajiya-Kay anisotropic sheen along the fibre tangent.

Cheap, and it is what actually sells the lint photo. In the visual target, tiles
04 → 05 add nothing but light.

### Layer 5 — Escapes (fins)
Long single hairs breaking the silhouette. Shells structurally cannot do this —
they clip at max height and read as concentric rings. Fin geometry along
silhouette edges (Lengyel 2001), or a few hero hair cards. Last thing to add.

### Surface profiles
Per-surface-type parameter sets rather than new shaders. Carpet is the
interesting case: dust sinks *between* carpet fibres rather than sitting on top,
so dust shells start at a negative height offset and interleave with the carpet's
own shells.

---

## Known issues

### Non-uniform scale stretches the dust

Scale a plane unevenly and the dust stretches with it. Not wanted — dust should
have a fixed real-world size regardless of how the surface it sits on is scaled.

There are **two** separate stretches happening, and they need different fixes.

**1. The strand pattern stretches (this is the visible one).**
`uvCell = IN.uv * _CellCount` uses raw mesh UVs, which run 0..1 across the mesh no
matter how big it is in world units. Scale a plane 10× in X and every cell becomes
10× wider in world space — circles become ovals, and dust-per-square-metre changes
with object scale. Two identically-dusty floors of different sizes will not match.

Candidate fixes, cheapest first:

- **Correct the UVs by object scale.** Extract scale from the object-to-world
  matrix (`length(unity_ObjectToWorld._m00_m10_m20)` and friends — valid per
  instance after `UNITY_SETUP_INSTANCE_ID`) and multiply it into the pattern
  lookup. Minimal change, keeps everything in UV space.
- **Drive the pattern from object-space position** instead of UV, sized in world
  units. Scale-independent by construction, and tiles continuously across
  adjacent surfaces. Breaks down on curved or vertical geometry.
- **Triplanar.** The general answer for arbitrary meshes, and the most expensive.

Whichever we pick, the **mask stays in UV space** — that's where `PaintCanvas`
paints, and it must keep following the surface. Pattern lookup and mask lookup
become two independent coordinate systems, which is fine and normal.

Note this only corrects for *object* scale. A mesh authored with uneven UV density
would still show it; a fully robust version measures actual world-units-per-UV.
Not worth solving until an asset actually needs it.

**2. Shell height stretches too (subtler).**
`posOS + normalOS * height` extrudes in **object space**, so the world-space shell
height is multiplied by whatever the scale is along that normal. On a flat plane
the normal is a single axis so it stays even, but on curved geometry the fluff gets
taller on some faces than others. The fix is to extrude in world units — divide
out the scale along the normal, or extrude after transforming.

`ComputeWorldBounds` already compensates by using `max(lossyScale)`, so culling is
safe either way. This is purely a look problem.

**Where this gets fixed:** Layer 1 rebuilds the pattern generation from scratch, so
the coordinate decision belongs there rather than as a patch now.

---

## Gotchas already paid for

Keep these; each cost real time.

- **`UNITY_GET_INSTANCE_ID` does not exist in SRP core.** It's a built-in-pipeline
  macro. The SRP idiom is `unity_InstanceID`, read *after* `UNITY_SETUP_INSTANCE_ID`,
  guarded by `#ifdef UNITY_INSTANCING_ENABLED` — `multi_compile_instancing` compiles
  a variant where the instancing macros expand to nothing.
- **`Graphics.RenderMeshPrimitives` carries no transform.** No matrix parameter at
  all; geometry lands at the world origin. `RenderMeshInstanced` with a matrix
  array is the right call.
- **`UNITY_SETUP_INSTANCE_ID` must precede any transform.** It's what fetches
  `unity_ObjectToWorld` from the per-instance array. Call `TransformObjectToHClip`
  first and everything collapses to the origin — and it looks exactly like a C# bug.
- **Two separate instancing gates.** The `#pragma` compiles the variant (compile
  time); the material's "Enable GPU Instancing" checkbox selects it (draw time).
  A checkbox can never fix a compile error.
- **Shader property names are case-sensitive and fail silently.** `_maxHeight` vs
  `_MaxHeight` gives a valid ID for a property nothing reads.
- **Unwritten varyings are garbage.** A missing `OUT.uv = IN.uv` reads as zero and
  breaks the strand test *and* the mask sample at once.
- **`MaterialPropertyBlock` values bypass the `Range()` clamp** in Properties. The
  C# range is the real limit.
- **Edit-mode `Update` doesn't tick continuously.** `RenderMeshInstanced` submits
  for one frame only, so dust flickers in the Scene view. Scene view → View
  Options overlay → Effects dropdown → **Always Refresh**. (Backtick opens the
  Overlays menu if that strip is hidden.)
- **A RenderTexture is runtime state, not authored data.** It isn't serialized and
  doesn't survive a domain reload. Nothing bridges a serialized field to GPU
  memory except code you write — which is why Inspector edits appeared to do
  nothing.

---

## Debugging method

The screen is the only debugger. Bisect rather than guess — first line of `frag`,
above everything:

```hlsl
return half4(1, 0, 0, 1);
```

Red shells → geometry and instancing are fine, problem is downstream in shading,
alpha or mask. Nothing → problem is upstream in C#: bounds, material, the draw
call. Then walk down one value at a time: `half4(IN.t.xxx, 1)`, then
`half4(IN.uv, 0, 1)`.

---

## References

- Lengyel et al., *Real-Time Fur over Arbitrary Surfaces* (2001) — shells and fins:
  https://hhoppe.com/fur.pdf
- NVIDIA, *Fur (Shells and Fins)* white paper:
  https://developer.download.nvidia.com/SDK/10/direct3d/Source/Fur/doc/FurShellsAndFins.pdf
- Kajiya & Kay anisotropic strand shading
- GiM, *An Introduction to Shell-Based Fur Technique*:
  https://gim.studio/animalia/an-introduction-to-shell-based-fur-technique/
- Garrett Gunnell, Shell Texturing (volumetric fur):
  https://github.com/GarrettGunnell/Shell-Texturing
- The Book of Shaders — patterns and noise fundamentals: https://thebookofshaders.com/
- Inigo Quilez, articles — noise, SDFs: https://iquilezles.org/articles/
