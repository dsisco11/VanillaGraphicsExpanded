# Entity, transparent and late-surface PBR coverage

## Draw ownership

`PbrSurfaceShaderPatches` handles the installed `standard`, `entityanimated`, `instanced` and
`chunktransparent` source families. `PbrDrawRouteHook` selects their lighting ownership on every
engine program use from `IRenderAPI.CurrentRenderStage` and `CurrentFrameBuffer`, using the engine's cached uniform table.
It does not cache uniform locations by recycled GL program IDs or allocate per draw.
Opaque/AfterOIT must target Primary; OIT must target Transparent. Offscreen inventory/atlas
draws retain vanilla shading even when called during a scene stage. The engine already tracks
that target, so this check does not query GPU state.

| Engine draw stage | Material and output contract |
| --- | --- |
| Opaque, non-OIT variant | Unlit linear material color plus world normal, RME and an empty Surface Cache patch ID; the existing direct/LumOn/composite passes light the receiver. |
| OIT | Forward PBR evaluated before `OIT(...)`; engine reveal/accumulation/glow outputs and alpha weighting remain owned by `oit.fsh`. No opaque outputs are declared at locations 4–7. |
| AfterOIT | Forward PBR evaluated in the actual mesh draw, after the earlier opaque composite; defined normal/material metadata remains available for debugging. |
| GUI, shadows and other stages | Original engine shading; the scene route is disabled. |

Installed `ModSystemFpHands.LoadShaders` registers its item program under `standard` and hand
program under `entityanimated`; both use `ALLOWDEPTHOFFSET`. They therefore receive the same
patches without relying on their generated program IDs. The depth-offset statements remain intact.
`SystemRenderEntities` registers opaque entities at order 0.4 and AfterOIT at 0.7. Its AfterOIT
callback invokes `EntityRenderer.DoRender3DAfterOIT`; held items use the standard-derived programs.
Evidence is the installed shader files and the class IL exports described in
[the baseline audit](PBR.BaselineShading.Contracts.md).

## Material and lighting

Vertex lighting still runs for the engine's glow, alpha and shadow-coordinate side outputs.
For scene draws only, RGB is replaced by the authored mesh tint (`renderColor * colorIn` for
animated entities, `rgbaTint * colorIn` for standard meshes). Instanced and transparent terrain
receive neutral RGB because their incoming RGB carries lighting rather than material tint.
Fragment capture preserves texture/overlay selection, color maps, entity frost/damage effects
and coverage. Standard thermal tint and partial-glow tint remain, but the legacy no-bloom RGB
brightness boost is excluded from captured albedo. Color is decoded once before BRDF evaluation. Transparent terrain retains atlas
material lookup, normal maps and atlas-safe parallax mapping.

Material capture intercepts the incoming RGB of the engine's fog/lighting helpers. Scene calls
save that unlit color and return unchanged before the helper can apply legacy lighting or call
nested fog helpers. Capture uses a per-fragment GLSL temporary, with no new texture or buffer.
GUI calls execute the original helper bodies. There is no search for a
`murkiness` declaration, and instanced meshes do not resample their texture for capture.
Final PBR evaluation/publication stays after the remaining appearance code and before OIT,
so underwater/reflection code cannot overwrite the result and alpha tests/depth offsets still run.
Standard's later damage multiplier is applied to the captured color once before decoding.

Generic/animated meshes retain the existing generic material policy: roughness 0.5, metallic from
render flags and emission from `glowLevel`. This task does not invent per-entity texture material
definitions or reinterpret block-atlas material textures as entity-atlas data.

Automatic standalone/LumOn selection and the local environment approximation are documented in
[PBR.LightingModes.md](PBR.LightingModes.md).

The forward evaluator shares `pbr_direct_brdf.glsl` with the deferred direct-light shader. It uses
the draw's actual view/model-view transform, not the camera's base transform, to compare point
lights with view-space receiver positions. Normals and BRDF directions remain in world axes.
Engine near/far shadow coordinates provide comparison-sampled visibility with no vanilla
half-shadow brightness floor. Visibility affects sunlight only; emission and local lights remain
independent. Forward sunlight includes the engine ambient/sun RGB multiplied by the mesh's sky
exposure; sky exposure alone must not produce daytime white lighting at night. Fog is blended
in linear color before the existing display transform.

The forward fallback uses the mesh's supplied block irradiance and sunlight availability. It does
not sample screen-space LumOn illumination belonging to an opaque surface behind glass or hands.
Adding a shared environmental/radiance-cache policy for these receivers belongs with the next
LumOn-compatible/standalone lighting-mode task. This is not a claim of traced GI on transparent
receivers or physically calibrated engine light units.

## Transparency and display boundary

The current primary target and engine OIT merge consume display-space color. Forward lighting
therefore uses the same unit-exposure Reinhard/sRGB resolve as opaque composition before those
existing blends. This establishes matching color conventions, not complete scene-linear HDR
transparency; moving the resolve after all blending belongs to the full-scene HDR task.

`chunkliquid` is deliberately excluded from opaque G-buffer patching. Its original OIT lighting,
flow, reflection and underwater code remain intact until the PBR liquid task replaces those optics.
It must not declare VGE normal/material outputs over OIT accumulation locations 4 and 5.

## Validation

Subagent-run validation passed, covering 47 distinct cases:

- 19 installed-source driver compile/link cases across the four families, including entity OIT,
  shadow qualities 0/1/2, first-person depth offsets, SSBO terrain, SSAO and standard partial glow.
  Linked fragment output checks verify OIT locations 4/5 and absence of opaque material outputs
  in OIT variants. Source checks verify publication occurs before the actual OIT call.
- Six numerical forward cases: full sun occlusion, single emission, view-space point lights,
  rotated view, colored sun and zero sunlight. Alpha is preserved.
- Ten terrain capture regressions and ten existing deferred direct-lighting cases.
- Two routing cases exercise every engine stage against correct/wrong/offscreen/missing targets
  and distinct wrappers referring to the same framebuffer identity.

Receipts: `artifacts/PbrColor/pbr-surfaces-final.trx` (36/36),
`artifacts/PbrColor/pbr-direct-extraction.trx` (10/10), and
`artifacts/PbrColor/pbr-scene-target-guard.trx` (21/21). These runs overlap: the last run replaces
the earlier routing case with two and rechecks the 19 installed variants after the final guard
and late metadata changes. The production shader catalog also rebuilt successfully: 153 stages,
387 variants.

The validation review identified missing sunlight color/time dependence and ambiguity for
offscreen rendering during a scene callback. Both were corrected and revalidated. No further
confirmed blocker remained. Headless tests do not execute the full engine render loop or prove
live appearance/performance; no game process was launched.

Helper-interception refactor validation: `artifacts/PbrColor/pbr-helper-capture.trx` passed 31/31
(19 installed variants, six forward numerical cases, two routing cases and four initial capture
cases). `pbr-helper-capture-routes.trx` then passed the expanded six capture cases across GUI,
deferred and forward routes, replacing the earlier four. These 33 distinct checks verify renamed/
moved water locals, later RGB mutation, preserved alpha and standard damage applied once.
Source review confirmed all four installed shader families call an intercepted lighting helper
before publication. The opaque terrain-specific capture patch is unchanged by this refactor.
