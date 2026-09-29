# Held-light attachment positioning

## Cause and engine contract

The installed engine's `SystemRenderPlayerEffects.onBeforeRender` collects each luminous entity using
`entity.LightHsv` and passes `entity.Pos` to `AddPointLight(byte[], EntityPos)`. For players this is the
feet position. `EntityPlayer.LightHsv` merges the hand emissions and its private `baseLightHsv`, so the
published point contains no hand identity or attachment offset.

`AddPointLight` transforms `(Pos.X, Pos.InternalY, Pos.Z, 1)` through `CurrentModelViewMatrixd` and
publishes view-space coordinates in `PointLights3`. Vanilla terrain uses distance attenuation without
the PBR surface-facing factor. VGE's normal-dependent direct BRDF makes the low source especially
visible on horizontal terrain. The coordinate convention in the direct-light shader is correct;
changing it or weakening the normal term would not repair source placement.

These findings were checked against the installed `VintagestoryLib.dll`, `VSEssentials.dll`, and
`VintagestoryAPI.dll`, and the local API source on 2026-09-29. The relevant engine methods are:

- `SystemRenderPlayerEffects.onBeforeRender` and both `AddPointLight` overloads.
- `SystemRenderEntities.OnBeforeRender`.
- `EntityPlayer.LightHsv`.
- `EntityPlayerShapeRenderer.loadModelMatrixForPlayer` and `RenderHeldItem`.
- `EntityShapeRenderer.RenderItem`.

## Collection and timing

The engine collects lights at `Before` order 0.1. Entity renderer preparation and animation advancement
occur at order 0.4. A held-item matrix retained from drawing therefore belongs to an earlier invocation
and may even describe a shadow pass.

`HeldLightSources` preserves the engine collection loop, nearby-entity predicate, ordering, registered
`IPointLight` loop, HSV conversion, and dynamic-light limit. A checked transpiler replaces only the
entity-light call so it also receives entity identity. For the standard player emission getter:

1. Dark hands keep the original combined result, including the engine's spawn-glow policy.
2. Emitting hands become separate entries, each using its item's dynamic `GetLightHsv` result.
3. An active `baseLightHsv` remains at the entity position. The merged hand emission is not added again.
4. Each admitted hand starts at a body-height fallback and is recorded for attachment resolution.

A VGE `HeldLightRenderer` registered at `Before` order 0.45 resolves the current attachment positions
after the engine's order-0.4 animation callback and before scene drawing consumes the shared light arrays.
It updates only admitted hand positions, leaving
colors and other light entries alone. Pending entries clear at collection start and after completion.
No attachment matrix is retained between frames.

The world-to-view matrix is copied during collection: `CurrentModelViewMatrixd` returns a shared engine
scratch array, not independently owned storage. The final world-to-view multiplication uses doubles
before storing shader floats. Neither VGE nor vanilla consumers require a coordinate-space change.

## Attachment evaluation

`HeldLightAttachment` reads the renderer through `player.Properties.Client.Renderer`, selects the normal
camera's public first- or third-person animation manager, and reads its current hand attachment pose.
`GetItemStackRenderInfo` supplies the item transform with zero additional render delta. Hot items held
with tongs use the left-hand pose and the item's tong transform, matching the engine's selection rules.

`HeldLightPlayerTransform` builds an independent matrix from public player, mount, camera, and renderer
state. It follows the engine's geometry conventions for shape rotation, size, seat transforms, swivel,
and first-person pitch. It uses current body orientation rather than the visual renderer's private yaw
smoothing, so rapid turns can produce a temporary difference from the displayed hand. The model transform
is checked against the installed engine at settled orientation; attachment/item composition is checked
against installed `RenderItem` IL. No renderer clone, private model-matrix invocation, or draw interception
is needed.

Player/item composition and hand-projection correction use `System.Numerics.Matrix4x4` and `Vector4`.
Engine matrix arrays are read at API boundaries in the equivalent row-vector convention; composition order
is reversed accordingly. World-origin addition and final world-to-view publication retain double precision
to preserve sub-block offsets at large coordinates. Engine matrix operations remain in tests as an independent
reference for the Numerics implementation.

The emitter anchor is `CollectibleObject.TopMiddlePos`, the existing item-local anchor used by the
engine's held-item particles. It is not assumed that the hand grip is the flame. No new asset schema is
introduced. In first person, hand-FOV coordinates are mapped back into the normal camera projection,
matching the engine particle-position calculation, before adding `CameraPos`.

Attachment evaluation does not invoke perception effects or mutate `selfNowShadowPass`. Selecting the
normal camera's animation manager explicitly prevents a preceding shadow pass from selecting the pose.
Later visual perception effects can still move the rendered item relative to the calculated emitter.

## Remaining engine integration

Only `SystemRenderPlayerEffects.onBeforeRender` is Harmony-patched: a prefix captures collection context,
a checked transpiler redirects only the entity-light call,
and a finalizer contains attributed collection failures. Animation, item drawing, and perception methods
are not patched. The original collector still owns entity enumeration, standalone lights, and admission.

Engine access is bound once through delegates for `AddPointLight`, plus a cached
field accessor for the private `baseLightHsv` contribution. Emission-getter metadata is checked once per
entity type to preserve custom overrides. Harmony injects the collector's game field into its prefix.
Attachment evaluation uses public data and APIs; there is no per-frame reflective method invocation.

## Limits and fallback

### Failure containment

`HeldLightSystem` installs this feature explicitly under its own Harmony ID,
`vanillagraphicsexpanded.heldlighting`; the hooks are excluded from the mod-wide `PatchAll` scan.
The mod composition root starts and stops this owner alongside its other rendering resources.

A held-light exception disables the feature for the remainder of the mod session and logs the operation
and full `Exception.ToString()` output, including the stack and inner exceptions. Startup installation
is also guarded: a compatibility failure in the transpiler removes any hooks already installed by this
owner and unregisters the renderer. There is no per-frame retry or automatic re-enable.

Collection failures are attributed inside the held-light wrapper and handled by a finalizer on the
collector. Attachment failures are caught at the registered renderer boundary. Failures outside the wrapped operations continue
through the engine's normal exception path; this is not a global exception suppressor.

On failure the subsystem unregisters its renderer, removes only its own patches with
`UnpatchAll(PatchId)`, and clears pending frame work and engine references. Other VGE and third-party
patch owners remain installed. The failure frame may retain partial held-light output or an interrupted
collection. The next normal engine collection restores vanilla lighting and capacity.

Collection is never replayed, and perception callbacks are neither redirected nor invoked by VGE.
If collection fails before the engine's perception update, that update is also skipped for the failure
frame. A patch-removal failure is logged, and remaining callbacks stay disabled/pass-through.
Logger failures cannot escape the shutdown boundary.

### Attachment fallback

- A missing renderer/attachment, hidden first-person hands, or a custom player renderer uses
  `Pos + (0, 0.75 * LocalEyePos.Y, 0)` for the held entry. This is an explicit body-relative fallback,
  not a claim of precise custom-renderer attachment support.
- A player class overriding the emission getter keeps its original combined emission; VGE cannot
  reliably separate arbitrary custom getter contributions.
- `TopMiddlePos` is a useful existing emitter anchor, but an unusual item may define it away from its
  visible luminous part. Such assets need their anchor checked.
- Two hands consume two dynamic-light slots. The existing engine limit remains authoritative.
- Attachment evaluation invokes the item's render-info callback an additional time with zero delta.
  Compatibility with mods whose callback has non-idempotent side effects needs live checking.
- Attachment evaluation precedes shadow and item drawing. Later render-only state changes can affect visible
  placement; this does not use stale item matrices or advance the animation manager again.

## Validation

Shutdown-only validation on 2026-09-29: **65 passed, 0 failed, 0 skipped**, including 23 held-light cases,
with no held-light compiler warnings. The obsolete collector-replay failure case was removed.
Output: `artifacts/held-light-shutdown-validation.log`. Earlier counts below describe the preceding implementation.

`HeldLightTests` covers installed HSV conversion, independent hands and base emission, nonplayer and
dark-hand passthrough, dynamic-light capacity, large-coordinate view conversion, hand-FOV unprojection,
and matrix composition against installed item-renderer IL. Its lifecycle cases cover first-person,
immersive first-person, third-person, and remote-player placement, registered standalone lights,
perception callback isolation, and unchanged shadow state. The lifecycle regression runs the installed
collector with production Harmony patches. Its animation-boundary fixture supplies a changed current
pose and overwrites the engine matrix scratch array before the registered renderer executes.
It checks that the old model matrix is ignored, live renderer state is preserved, and removal of the
held emitters does not retain pending work into the next frame.

Renderer architecture validation on 2026-09-29: build succeeded; **66 passed, 0 failed, 0 skipped** with the
combined filter below, including 24 held-light cases. Output: `artifacts/held-light-recovery-validation.log`.
The focused held-light run is recorded in `artifacts/held-light-renderer-tests.log`. Tests verify that only
the collector is patched, renderer registration/removal follows subsystem lifetime, and disposed callbacks
are inert. Six model-transform comparisons cover the three camera modes with and without a mount.
The System.Numerics conversion also passed all 24 held-light and 66 combined cases on 2026-09-29,
with no held-light or MatrixHelper compiler warnings. Logs: `artifacts/held-light-numerics-tests.log`
and `artifacts/held-light-numerics-validation.log`.
Fault-injection cases cover failure after the first hand is admitted, a later attachment failure,
and a startup transpiler mismatch. Assertions check actual Harmony owner removal, renderer unregistration,
preservation of foreign patches, full exception logging, and vanilla positions/capacity on the next normal
collection. An attachment failure arms an engine-query exception to verify collection is not replayed.
An unrelated engine-query exception is verified to propagate.

```powershell
dotnet test .\VanillaGraphicsExpanded.Tests\VanillaGraphicsExpanded.Tests.csproj --no-restore --filter "FullyQualifiedName~HeldLightTests|FullyQualifiedName~GBufferExternalFramebufferTests|FullyQualifiedName~PbrModeLifecycleTests|FullyQualifiedName~PbrDirectLightingShadowTests|FullyQualifiedName~PbrTerrainCaptureGpuTests|FullyQualifiedName~PbrDirectLightingFunctionalTests" -v minimal
```

Live visual acceptance remains user-run: compare left/right/both hands, swapping or extinguishing
items, first/third/immersive-person cameras, looking up/down, crouching, walking, mounted players,
remote players, and illuminated terrain immediately underfoot. Check a dropped emitter and a
registered standalone point light alongside the held item. Automated checks do not establish live
placement accuracy or frame cost.
