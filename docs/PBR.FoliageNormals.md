# Foliage surface normals

## Cause

Installed-engine inspection on 2026-09-29 found that `CrossTesselator.DrawCross` writes
`BlockFacing.UP.NormalPackedFlags` for cross plants. `ShapeTesselator.TesselateShapeElement`
also writes UP for `Shade == false` without gradient shading. Tall grass, ferns, and normal
leaves use the `OpaqueNoCull` render pass. These legacy lighting normals are not necessarily
geometric surface normals.

VGE previously used the unpacked normal for both the normal-map tangent frame and G-buffer.
Its direct BRDF already rejects nonpositive N dot L for diffuse and specular, but upward
plant normals incorrectly make vertical faces receive overhead sunlight.

## Correction

`MeshPoolClassifier` builds shared reference-identity hash sets for `OpaqueNoCull`/`BlendNoCull` and
opaque/topsoil displacement managers. Postfixes on `ChunkRenderer` construction and
`RuntimeAddBlockTextureAtlas` refresh membership after the engine populates or extends its
table, including additions within the same outer array. Installed-engine inspection confirmed
both operations create managers through `AddPoolsForAtlasAndPass`.

The render scope resolves the cached snapshot once; manager draws use hash-set membership
without scanning pool arrays. A one-time lazy build supports an already-existing renderer.
A weak table keyed by the engine pool table avoids retaining retired worlds. Replacement
tables get independent snapshots; normal render-scope restoration releases the active reference.
Both foliage and displacement use this shared classification.

`TerrainSurfaceNormals` publishes `vge_twoSidedTerrain` at the existing manager draw boundary,
including false for other managers sharing the shader. No mesh mutation is introduced, and
selection does not depend on tessellation being enabled.

Shader identification uses `ShaderCapabilities`, not uniform-name probing. The normal patch
explicitly declares `TwoSidedSurfaceNormals`; successful engine compilation publishes it against
the managed shader owner and its final program ID. Displacement installation publishes
`TerrainDisplacement` through the same registry, while its family-readiness policy stays with
the displacement owner. Reload, disposal, failed compilation, and original-source fallback
invalidate the corresponding registration. The draw path only binds the normal-policy uniform
when the active executable has the registered normal capability.

At the start of the terrain fragment main, before alpha discard or parallax UV changes,
`VgeTerrainNormal` derives a visible-side face normal from world-position derivatives.
Within the selected pass, near-UP legacy normals use that geometric normal. Other authored
normals are preserved except for flipping to the visible hemisphere on back-facing surfaces.
Degenerate derivatives retain the authored normal with a front/back fallback. Opaque and
topsoil surfaces retain their existing shading normals.

The corrected normal feeds the tangent frame, normal mapping, and material G-buffer, so both
direct diffuse and specular consume the same corrected orientation. Existing base-surface
patch identity is unchanged. This does not add leaf transmission or subsurface scattering.

## Validation

Shader capability registration passed 91 focused tests on 2026-09-29
(`artifacts/shader-capabilities-validation.log`), covering patch declarations, successful
publication, owner/program identity, failed compilation, original-source recovery, installed
surface and terrain shader interfaces, and ordinary-mode executables. The previously recorded
adaptive-family capability-gate failures below were not rerun by this filter.

The shared membership-cache change passed 20 focused pool-policy and terrain-capture tests on
2026-09-29 (`artifacts/terrain-pool-membership-validation.log`). Tests cover both consumers,
same-table replacement, cache reuse, and independent replacement tables. No frame-time speedup
has been measured.

GPU regressions exercise upward-normal correction, opposite viewing directions, preservation
of authored smooth normals, and exclusion of ordinary opaque surfaces. A pool-identity test
checks selection and reset outside the no-cull pool. Existing direct-light and shader-patching
regressions are run alongside these tests.

Validation on 2026-09-29: the focused suite passed 93 tests with one existing skipped test
(`artifacts/foliage-normal-validation.log`). The additional installed-surface/tessellation run
passed 64 tests; three tessellation lifecycle tests failed at the OpenGL 4.0 capability gate
before adaptive shader compilation (`artifacts/foliage-installed-shader-validation.log`).
The installed-surface shader tests reported no failures. These results do not establish that
the complete tessellation lifecycle suite passes.

Live acceptance remains user-run: inspect SceneNormal and direct diffuse/specular views on
tall grass, ferns, and leaves from both sides with sun ahead, behind, and overhead. Compare
ordinary opaque blocks and topsoil; also check wind motion and normal maps on/off. No live
visual or performance result is claimed by these automated tests.

## Transmitting materials

Positive material-atlas transmission now selects the visible geometric normal unconditionally, before constructing the normal-map tangent frame. This bypasses the upward-normal heuristic and pool restriction for transmitting terrain, including gradient-shaded and sheltered leaves. Zero-transmission materials retain the existing two-sided correction. Transmission consequently opts into flat geometric base shading; normal maps still perturb that base normal.
