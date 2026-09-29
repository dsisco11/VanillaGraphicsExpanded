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

A postfix on `SystemRenderEntities.OnBeforeRender` resolves the current attachment positions before
scene drawing consumes the shared light arrays. It updates only admitted hand positions, leaving
colors and other light entries alone. Pending entries clear at collection start and after completion.
No attachment matrix is retained between frames.

The world-to-view matrix is copied during collection: `CurrentModelViewMatrixd` returns a shared engine
scratch array, not independently owned storage. The final world-to-view multiplication uses doubles
before storing shader floats. Neither VGE nor vanilla consumers require a coordinate-space change.

## Attachment evaluation

`HeldLightAttachment` takes a temporary shallow snapshot of the standard player renderer and gives it
an independent model matrix. The installed `loadModelMatrixForPlayer` computes current placement on
that snapshot, including body rotation, crouch/mount offsets, size, and first-person pitch policy.
Smoothing fields written by this calculation belong to the snapshot, not the live renderer.

The installed virtual `RenderHeldItem` selects the hand and item transform, including its tong path.
It runs with zero additional render delta. A narrowly scoped `RenderItem` prefix captures its placement
arguments and prevents shader operations, drawing, and particle spawning for the snapshot only.
The attachment/item matrix composition is checked against the installed `RenderItem` IL in a regression.

The emitter anchor is `CollectibleObject.TopMiddlePos`, the existing item-local anchor used by the
engine's held-item particles. It is not assumed that the hand grip is the flame. No new asset schema is
introduced. In first person, hand-FOV coordinates are mapped back into the normal camera projection,
matching the engine particle-position calculation, before adding `CameraPos`.

The engine's `ApplyToTpPlayer` perception callback can mutate shared animation poses (the drunk effect
does this). It is suppressed during temporary evaluation and left to the normal renderer invocation.
The temporary change of `selfNowShadowPass` is restored in `finally`, as is the capture scope.

## Limits and fallback

### Failure containment

`HeldLightSystem` installs this feature explicitly under its own Harmony ID,
`vanillagraphicsexpanded.heldlighting`; the hooks are excluded from the mod-wide `PatchAll` scan.
The mod composition root starts and stops this owner alongside its other rendering resources.

A held-light exception disables the feature for the remainder of the mod session and logs the operation
and full `Exception.ToString()` output, including the stack and inner exceptions. Startup installation
is also guarded: a compatibility failure in the transpiler removes any hooks already installed by this
owner. There is no per-frame retry or automatic re-enable.

Collection failures are attributed inside the held-light wrapper and handled by a finalizer on the
collector. Attachment failures are caught at the post-animation boundary, after the temporary renderer's
`finally` blocks restore capture and shadow state. Failures outside the wrapped operations continue
through the engine's normal exception path; this is not a global exception suppressor.

Recovery runs the installed collector once with held-light callbacks disabled. Its normal reset removes
partially published entries, and complete enumeration restores the original merged player lights and
any standalone lights excluded by the extra hand slots. Recovery uses an owned copy of the original
collection matrix and restores the surrounding matrix stack afterward. If normal collection already
updated perception, recovery suppresses that callback; otherwise it runs once during recovery.

After recovery the subsystem removes only its own patches and clears pending frame work and engine
references. Other VGE and third-party patch owners remain installed. If vanilla recovery itself fails,
that exception is also logged and the partial dynamic-light count is cleared for the current frame;
subsequent frames use the vanilla collector. Harmony removes this owner's patches with `UnpatchAll(PatchId)`.
A patch-removal failure is logged, and any remaining callbacks stay disabled/pass-through.
Logger failures cannot escape the recovery boundary.

### Attachment fallback

- A missing renderer/attachment, hidden first-person hands, or a custom player renderer uses
  `Pos + (0, 0.75 * LocalEyePos.Y, 0)` for the held entry. This is an explicit body-relative fallback,
  not a claim of precise custom-renderer attachment support.
- A player class overriding the emission getter keeps its original combined emission; VGE cannot
  reliably separate arbitrary custom getter contributions.
- `TopMiddlePos` is a useful existing emitter anchor, but an unusual item may define it away from its
  visible luminous part. Such assets need their anchor checked.
- Two hands consume two dynamic-light slots. The existing engine limit remains authoritative.
- Snapshot evaluation invokes the item's render-info callback an additional time with zero delta.
  Compatibility with mods whose callback has non-idempotent side effects needs live checking.
- The snapshot precedes shadow and item drawing. Later render-only state changes can affect visible
  placement; this does not use stale item matrices or advance the animation manager again.

## Validation

`HeldLightTests` covers installed HSV conversion, independent hands and base emission, nonplayer and
dark-hand passthrough, dynamic-light capacity, large-coordinate view conversion, hand-FOV unprojection,
and matrix composition against installed item-renderer IL. Its lifecycle cases cover first-person,
immersive first-person, third-person, and remote-player placement, registered standalone lights,
perception callback isolation, and shadow-state restoration. The lifecycle regression runs the installed
collector with production Harmony patches. Its animation-boundary fixture supplies a changed current
pose and overwrites the engine matrix scratch array before the production completion hook executes.
It checks that the old model matrix is ignored, live renderer state is preserved, and removal of the
held emitters does not retain pending work into the next frame.

Initial attachment validation on 2026-09-29: build succeeded; **54 passed, 0 failed, 0 skipped** in the combined focused
suite below, including 12 held-light cases. Output: `artifacts/held-light-validation.log`.

Recovery validation on 2026-09-29: build succeeded; **59 passed, 0 failed, 0 skipped** with the same
combined filter, including 17 held-light cases. Output: `artifacts/held-light-recovery-validation.log`.
Fault-injection cases cover a failure after the first hand is admitted, a later attachment failure,
failure of the vanilla recovery itself, and a startup transpiler mismatch after other hooks have been
installed. Assertions check actual Harmony owner removal, preservation of foreign patches, restored
vanilla positions and light capacity, matrix/shadow-state restoration, full exception logging, and
perception updating exactly once. An unrelated engine-query exception is verified to propagate.

```powershell
dotnet test .\VanillaGraphicsExpanded.Tests\VanillaGraphicsExpanded.Tests.csproj --no-restore --filter "FullyQualifiedName~HeldLightTests|FullyQualifiedName~GBufferExternalFramebufferTests|FullyQualifiedName~PbrModeLifecycleTests|FullyQualifiedName~PbrDirectLightingShadowTests|FullyQualifiedName~PbrTerrainCaptureGpuTests|FullyQualifiedName~PbrDirectLightingFunctionalTests" -v minimal
```

Live visual acceptance remains user-run: compare left/right/both hands, swapping or extinguishing
items, first/third/immersive-person cameras, looking up/down, crouching, walking, mounted players,
remote players, and illuminated terrain immediately underfoot. Check a dropped emitter and a
registered standalone point light alongside the held item. Automated checks do not establish live
placement accuracy or frame cost.
