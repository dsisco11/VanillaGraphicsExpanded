# PBR lighting modes

`LumOn.Enabled` selects the composition variant before shader preparation. Both modes retain
the existing direct sun, point-light, emission and display-resolve paths. Disabling LumOn does
not change the sun shadow comparison or introduce a minimum direct-light visibility.

## Standalone environment

Opaque terrain and supported entity/late shader families publish local environment irradiance
to primary attachment 7 (`RGBA16F`). The value combines the engine's local block-light RGB
with 0.35 times its ambient sun RGB multiplied by local sky-light availability. The shared
`pbr_environment.glsl` evaluates diffuse with metallic/Fresnel weighting and a roughness-dependent
specular approximation. Forward surfaces evaluate the same helper directly.

This is an explicit approximation, not traced indirect lighting or a reflection environment map.
The sky coefficient is a calibration choice, not a physical atmospheric integral. An enclosed
surface with zero local sky and block light gets zero environment illumination. Partial coverage,
vertex interpolation and the engine's block-light propagation can still leak around thin geometry;
there is no new geometric AO term. The future sky task owns atmospheric calibration and directional
environment lighting. A fully rough metal can remain dark without an environment reflection model.

The extra target costs eight bytes per pixel of storage plus its write/read bandwidth. It remains
allocated across mode switches as part of the shared G-buffer; switching does not resize or recreate
terrain targets. Existing material, normal/height and patch-identity channels retain their meanings.

## LumOn composition and lifecycle

The LumOn variant consumes published indirect illumination once and does not sample the standalone
environment attachment. Composition asks the owning system for its current buffer manager, so a
renderer created while LumOn is disabled can use a manager created after enabling it.

The lighting renderer invalidates publication at the start of each frame and publishes only after
the main lighting pass chain succeeds. Clearing history or disposing indirect targets also invalidates
publication. If lighting is unavailable, composition retains direct light and emission with zero GI;
it does not switch to a standalone shader variant based on texture readiness. This prevents stale
results after failed updates and avoids readiness-driven shader generations.

Startup with LumOn disabled retains the owners' existing allocation gates. A disabled lighting
renderer returns before jitter creation and lighting work. Previously allocated GI resources remain
owned by LumOn until its normal teardown; the PBR renderer neither creates nor disposes them.
Shader reload retains demand preparation: configuration is applied before the next `EnsureReady`.

Engine forward shaders specialize `VGE_PBR_FORWARD_LUMON` at source preparation; there is no
per-draw LumOn-mode uniform. `vge_pbrRoute` remains dynamic because the same engine program serves
GUI, deferred and forward draws. Changing `LumOn.Enabled` joins the existing coalesced main-thread
shader reload used for material options. Unchanged configuration does not request a reload.

`PbrShaderLightingMode` freezes the engine generation's mode and supplies it to production deferred
composition. A queued configuration change cannot switch composition ahead of the engine programs.
The reload prefix captures the new mode before compilation; initial asset setup resets the snapshot.
Startup changes wait until world finalization. If engine reloads are suppressed, the snapshot stays
unchanged and the request remains pending until a later config event or world finalization.

The installed engine's reload is destructive: it disposes registered programs before rebuilding,
and a false result does not restore old programs. All replacements use the captured target mode,
including when some compilations fail; we log the failure and do not claim rollback or success.
Correct the shader error and reload again to recover. No transactional engine reload was added.

Forward transparent/late surfaces in LumOn mode retain their existing local block-light fallback;
they do not sample opaque screen-space GI behind the forward receiver. This change does not add
traced indirect lighting for those surfaces. GUI and liquid routes retain their existing policies.

## Validation

Forward specialization follow-up: `artifacts/PbrColor/pbr-forward-compiletime-final.trx` passed
59/59 checks: 38 installed shader variants across both modes, ten forward numerical cases,
six capture cases, two routing cases, two generation-state cases and one mode lifecycle case.
Generation checks cover queued mode changes, reload adoption and suppression. Engine reload
failure behavior was verified through installed IL (`ShaderRegistry.ReloadShaders.il` and
`ShaderRegistry.LoadRegistered.il` under `artifacts/PbrColor`), not by launching the game.
Review identified and corrected premature mode adoption during suppressed reloads.

The shader catalog rebuilt successfully (153 stages, 387 variants). All 64 distinct focused cases
passed across `artifacts/PbrColor/pbr-modes-final.trx` (63 passes) and
`artifacts/PbrColor/pbr-modes-lifecycle-final.trx` (the remaining lifecycle case). The first lifecycle
run incorrectly expected initial `EnsureBuffers` to report a stable frame; the fixture now respects
its documented initial-allocation return value. No production change was needed for that assertion.

Coverage includes installed surface variants and attachment 7/OIT exclusion, standalone environment
and material response, GI/environment exclusivity, shadowed sky and sealed-space behavior, point
lights, emission, HDR/display, startup-off/later-provider discovery, initial mode selection,
disable/reload, unpublished GI rejection, history/recreation invalidation, and attachment resize/clear.

The validation agent's separate source review found and verified fixes for attachment-ID bookkeeping
after resize and the environment sampler's missing fixed uniform location. No further confirmed
blocker remained. Publication failure is tested at the buffer-owner boundary and source-reviewed
through the lighting renderer; the suite does not deliberately sabotage a live upsample framebuffer.
No game process was launched, and numerical tests do not establish live appearance or performance.
