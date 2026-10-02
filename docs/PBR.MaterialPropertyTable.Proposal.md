# Unified PBR material-property table

Status: proposed; implementation requires review and explicit approval.

## Intent

Replace the separate material-property textures with one shared uniform buffer object (UBO) containing an array of resolved material records, and an R32UI material-ID texture mapping atlas texels to record indices. Generate material noise in production shaders instead of baking noise into property textures.

The expected benefits are fewer property samplers, less atlas-sized property storage, and property updates without regenerating noisy atlas tiles. These are expectations to measure, not established performance results.

## Scope and existing behavior

The current material atlas stores roughness, metallic, emissive and transmission in RGBA16F; see [transmission](PBR.Transmission.md) and the [atlas build pipeline](MaterialAtlas.BuildPipeline.md). Water has additional index and coefficient textures. Displacement also requires per-tile metadata. All such material-property lookup resources should move into the common table and ID lookup.

Engine diffuse/albedo artwork, normal/height maps and other genuinely spatial image data remain textures. Any constant diffuse tint or other constant shading value belongs in the material record. This proposal changes property storage and evaluation, while preserving material resolution, inheritance, face selection, priorities, override scales and physical units.

## Resource contract

- One shared material UBO contains every active resolved property record across atlas pages. It is owned by the material system and reused across draws.
- One R32UI ID texture accompanies each engine atlas page. Together these page textures form the single logical material-ID atlas; drawing a page binds its ID texture and the same shared UBO. Review must confirm this interpretation of the multi-page engine atlas.
- Each ID texel contains an exact unsigned record index. Index zero selects an explicit neutral/default record, including unfilled regions. The record defines defaults rather than relying on zero-filled memory to produce correct shading.
- Shaders use an integer sampler and exact integer fetches. IDs are never linearly filtered or averaged into mip levels. The lookup uses the same atlas region and boundary rules as the sampled surface; filtering and distant sampling behavior require explicit validation.
- A generation publishes its UBO, record count and ID pages coherently. IDs are valid only for that generation. Updates cannot expose new IDs against an old table or retire resources still used by submitted draws.

## Record contents and layout

Use one documented CPU/GLSL layout with explicit std140-compatible vec4/uvec4 fields and array stride. The initial record should cover:

- Base roughness, metallic, emissive and transmission, plus any constant diffuse tint supported by authoring.
- Noise amplitudes and the parameters needed to evaluate existing property noise in production shaders.
- Stable noise seed derived from the asset key and established salts; tile bounds or an equivalent atlas-to-local coordinate transform.
- Effective RGB water absorption and scattering coefficients in inverse metres, anisotropy and an explicit medium-presence flag. Preserve the existing density application and validation contract.
- Displacement amplitude and any tile metadata required by displacement consumers.
- Flags or other resolved values required by the audited production consumers.

This is a proposed content list, not a frozen binary layout. Review must establish the complete consumer inventory and calculate the actual stride before approval. Authoring metadata such as notes and priority stays on the CPU after resolution.

A record represents a resolved atlas binding, not necessarily a unique authored material name. The same authored material can require distinct records for face-specific values, tile coordinates or noise seeds. Deduplicate only records with identical complete evaluation behavior.

## Production-shader noise

Noise is not baked into the ID atlas or another property texture. Shared production shader code evaluates noise from record parameters, stable asset-derived seeds and tile-local coordinates, then applies the established scaling and clamping semantics. The ID texture describes material assignment rather than noise samples.

Audit the existing noise algorithm before defining the shader evaluator: preserve channel salts, amplitude meaning, coordinate frequency and ordering relative to override scales. Document any deliberately changed visual distribution for approval. Atlas packing, page reassignment and reload must not change the material's noise pattern.

All consumers needing the same effective properties must share the evaluation contract, including liquid, opaque, displacement and cached lighting paths where applicable. A CPU consumer needing evaluated properties uses the same resolved record and a documented equivalent evaluator. Normal-generation noise must be audited separately: retaining normal/height image storage does not authorize retaining baked material-property noise.

Measure shader evaluation cost, derivatives, aliasing and distant-view stability. Specify noise filtering without filtering material IDs; do not introduce frame-dependent seeds or view-dependent shimmer.

## Authored spatial property overrides

Authored property images are distinct from procedural noise. Inventory their use and preserve current channel, alpha-mask and scale semantics. Under the proposed resource contract, genuinely varying property values can be represented by distinct resolved records selected per texel by the ID atlas, with deduplication of identical payloads. This can greatly increase record count.

Approval must establish whether actual override assets fit the UBO and whether this representation preserves required precision and filtering behavior. If they do not, explicitly decide supported authoring restrictions or revise the architecture before implementation. Do not silently discard overrides, quantize their values, retain parallel property textures or substitute an SSBO for the requested single UBO.

## Capacity and publication

Query the supported uniform-block size and budget the complete record layout. Capacity is the available block bytes minus headers, divided by the std140 record stride, including the neutral record. A fixed shader array bound, CPU packing and runtime record count must agree. R32UI's index range does not remove the UBO capacity limit.

Collect record counts across representative worlds, multiple atlas pages, face overrides and authored property images. Define diagnostics and a deterministic capacity rejection policy before allocating or publishing a generation; never publish a truncated table. If required content exceeds the capacity, the proposal needs revision and approval.

Use the established material-system ownership, streaming and GPU resource retirement APIs. Keep the table persistent across draws instead of copying it through per-draw parameter updates. Decide whole-generation replacement versus bounded updates using measured reload and streaming workloads. Preserve cancellation, teardown, resize where relevant and coherent CPU snapshots used by other systems.

## Migration and validation

Inventory every property sampler and CPU reader, then migrate them to one common record/evaluation contract. Retire the old property textures and their binding descriptors when their consumers have migrated. Preserve generated shader-binding contracts and the existing synchronous/asynchronous atlas publication rules.

Version or invalidate affected property caches. Retain caches for spatial artwork and normal/height data where their payload remains valid. Changes to transmission, water coefficients or noise parameters must use current resolved records rather than stale cached property values.

Required validation includes CPU/GLSL packing and stride, integer IDs, neutral and out-of-range behavior, atlas seams, multiple pages, face resolution, stable noise across repacking, property override semantics, capacity exhaustion, reload/cancellation and resource retirement. Exercise opaque and liquid rendering, water transport, displacement and relevant LumOn modes. Builds and tests run through subagents; live visual acceptance remains user-run.

Compare the current system and the proposed system at matched workloads: property texture and UBO memory, sampler usage, upload bytes and calls, startup/reload time, shader/GPU time and noise stability. Include representative noisy materials and override-heavy content. Fewer samplers alone do not establish a net improvement.

## Approval decisions

Review must resolve the complete record layout, multi-page resource interpretation, supported UBO capacity, production noise algorithm and filtering, authored spatial override representation, consumer parity and publication ownership. Record accepted behavior changes and the measured validation plan. Obtain explicit user approval before deriving an implementation task list or beginning the refactor.
