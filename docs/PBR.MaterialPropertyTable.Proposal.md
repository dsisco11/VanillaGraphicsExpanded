# Shared material records and atlas-coordinate indirection

Status: proposed; implementation requires review and approval.

## Intent

Replace the baked PBR material-property atlas with two persistent shared UBOs: an atlas-cell-to-material-ID lookup table and a material-information table. Derive the lookup index from the surface's existing packed atlas coordinates using integer shifts and bit concatenation. Evaluate material noise during rendering using position-based RGB hashing and parameters stored in each material record.

This revision supersedes the earlier R32UI material-ID atlas proposal. No material-property or material-ID atlas texture is part of the proposed lookup path. Engine diffuse artwork and genuinely spatial normal/height data remain textures. Removing those images is not the purpose of this proposal.

Expected benefits are fewer material-property samplers, less atlas-sized property storage, and updates without baking procedural noise into textures. Upload, storage and GPU execution improvements must be measured.

## Current contracts and feasibility

Current material parameters are sampled through includes/vge_material.glsl and captured into the surface G-buffer material layer. Deferred consumers read that layer through includes/lumon_material.glsl. The existing VgeMaterialUBO declaration holds only one material and has no active publisher; it must become a shared record table rather than a per-draw material upload.

The existing AtlasSnapshot and AtlasRectResolver retain page dimensions and individual atlas rectangles. They do not establish that every entry has identical dimensions or power-of-two-aligned origins. Equal, power-of-two tile size is a proposed indexing precondition that must be verified against actual engine packing, padding, texture packs, animation and modded assets before implementation. Equal artwork size alone does not prove aligned packing pitch.

Preserve resolved material inheritance, face selection, priorities, property units and overrides. An atlas entry identifies a surface texture/material binding, not necessarily a unique block: different block faces may use different entries, and multiple blocks may share an entry. UV lookup alone cannot distinguish blocks sharing the same texture but requiring different materials; those cases need an explicit discriminator or a reviewed authoring restriction.

## Lookup algorithm

For a page with a validated grid of square cells of size S = 2^k texels and 2^b addressable columns:

1. Decode the existing packed UV representation into atlas texel coordinates, accounting for its actual quantization, page dimensions and padding convention.
2. Compute cellX = texelX >> k and cellY = texelY >> k.
3. Concatenate the integer coordinates: localIndex = (cellY << b) | cellX.
4. Select lookupIndex = pageBase + localIndex using the page identity already associated with the draw.
5. Read materialId from the indirection UBO and use it to index the shared material-information UBO.

Use a power-of-two row stride for bit concatenation; account for unused slots when a page has fewer columns. Validate coordinates before indexing. Interior interpolated UVs must remain inside the owning cell, including exact edges, atlas padding, rotated faces, partial faces and displaced UV sampling. Resolve identity from the original surface UV before parallax or filtering can cross into a neighboring entry. Define whether identity is resolved per vertex with flat propagation or per fragment after inspecting actual mesh/UV contracts.

Indices are deterministic within an atlas layout and publication generation. Repacking can change them; they are not persistent material identities. Material IDs reference resolved records, with zero reserved for an explicit neutral/default record. Stable asset-derived seeds are independent of table ordering and atlas placement.

If entries span multiple aligned cells, those cells may map to the same material ID. If two distinct entries occupy one cell, the proposed index is ambiguous. Investigate a smaller aligned cell size and its storage cost; do not silently assume compatibility, introduce a texture fallback, or drop content. Nonconforming packing requires revising this proposal before implementation.

## UBO layout and capacity

Use one published lookup UBO and one shared material-information UBO, with explicit std140 layouts, binding contracts and CPU packing. Reuse existing typed shader bindings, GPU resource ownership and capability APIs. The lookup UBO stores four unsigned IDs per uvec4; fetch vector index lookupIndex >> 2 and component lookupIndex & 3. Avoid a scalar uint array whose std140 array stride would waste space.

Illustrative payload arithmetic: a 4096 by 4096 atlas on a 32-texel grid needs 128 by 128 IDs, or 65,536 bytes before metadata. A 16-texel grid needs 262,144 bytes. Multiple pages add their slot counts. These are examples, not verified engine dimensions or supported block sizes. Query the device's uniform-block size and validate both tables independently; compact coordinate arithmetic does not guarantee either table fits.

Compute material capacity from the final record stride, metadata and supported block size. Align fixed shader bounds, runtime counts and CPU layouts. Publish no truncated tables. If representative content exceeds a single block, report the feasibility conflict and revise the design explicitly rather than silently substituting an SSBO or multiple per-draw tables.

## Material-information records

Evolve VgeMaterialUBO into a table of resolved material records containing:

- Base roughness, metallic, emission and sunlight transmission, plus supported constant color/tint factors.
- Noise channel amplitudes, spatial frequency/scale, stable seed and any offsets or shaping parameters required by the agreed evaluator.
- Flags and local-coordinate transforms where needed by surface evaluation.
- Water absorption/scattering coefficients, anisotropy and medium flags where applicable, preserving existing physical units and density semantics.
- Displacement amplitude and other material constants required by audited consumers.

Freeze a documented CPU/GLSL binary layout only after the consumer inventory and capacity analysis. Keep authoring-only metadata on the CPU. Deduplicate records only when their complete evaluation behavior is identical; separate IDs may be needed for resolved face overrides or coordinate-dependent semantics.

## Runtime procedural noise

Generate a deterministic three-channel hash from a stable surface position and material seed. Use independent channel salts for roughness, metallic and emission variation; describe any transmission variation explicitly. Store noise amplitudes and frequency in the record, then apply documented scale, override and physical clamping rules in shared shader code.

Choose and document the coordinate domain: block-local coordinates repeat within each block; stable world coordinates vary across blocks; object-local coordinates keep animated objects' patterns attached to their surfaces. Never hash camera-relative positions without restoring a stable origin. Atlas repacking, camera motion and frame index must not change a stationary surface's noise pattern.

Audit existing baked noise and describe any intentional distribution change from replacing it with simple RGB hashing. Define distance/footprint attenuation or filtering to avoid aliasing and shimmer. All forward, deferred, liquid, displacement and cached-lighting consumers must evaluate equivalent properties at the same surface point. Provide a matching CPU evaluator where required. Normal/height-generation noise remains a separate contract from material-property noise.

## Deferred rendering and G-buffer replacement

Remove the dedicated per-pixel material-property payload once every consumer can obtain a material record and evaluate its noise. This does not remove the need for surface identity in deferred passes: a fullscreen shader cannot recover the original atlas UV or material ID from depth alone.

The proposed deferred bridge is a compact exact material ID written by the geometry pass, using a suitable existing attachment/channel if its precision and ownership permit, otherwise an owned integer attachment. IDs are never blended or interpolated as material values. Establish exact clear, resolve and read semantics. This replaces stored roughness/metallic/emission/transmission values with identity; it is not a material-property atlas.

Reconstruct surface position from depth for world-space noise. If block/object-local noise requires additional orientation or identity, specify how that survives into deferred evaluation before removing existing data. Preserve albedo, normals, depth and unrelated surface channels. Forward consumers resolve IDs directly from their atlas coordinates. Audit AO transmission reads, direct lighting, composition, refraction, liquids, displacement, LumOn capture and debug views before retiring the material layer.

If eliminating every material-related screen-space attachment is required, a different visibility/identity reconstruction design is needed; that is not supplied by UV indirection alone.

## Authored spatial overrides

A single material ID per atlas cell cannot encode arbitrary per-texel roughness or other authored property images. Inventory current overrides and resolve their representation before migration. Constant overrides fit records; procedural variation fits record parameters. Arbitrary images require a separately approved representation or explicit authoring change. Do not silently discard images, quantize them, or preserve the old property atlas as an undocumented fallback.

## Ownership and publication

The existing material system owns coherent generations of page metadata, lookup entries and material records independently of LumOn. Publish once per material/atlas change and reuse bindings across draws. Keep lookup IDs and records from the same generation, preserve asynchronous cancellation and retire buffers only after dependent submissions no longer use them.

Coordinate generation changes with deferred material IDs and cached lighting data; no consumer may interpret old IDs through newly reordered records. Invalidate affected property caches while preserving valid artwork and normal/height caches. Avoid per-frame allocations, per-draw table uploads and rebaking property noise.

## Validation and review

Before implementation, verify real atlas layouts and packed vertex UV formats, padding and boundary rules, multi-page identity, material sharing, required record counts, authored overrides and both UBO capacities. Resolve the deferred identity format and noise coordinate domain. Use existing material-system APIs rather than introducing parallel registries.

Focused subagent-run tests must cover CPU/shader packing, integer decoding, cell boundaries, page isolation, default IDs, capacity rejection, face overrides, noise stability under camera motion/repacking, forward/deferred agreement, reload/cancellation, resource retirement and both lighting modes. Exercise liquids, transmission, displacement and spatial overrides explicitly.

Measure lookup and record storage, removed atlas/G-buffer bytes, sampler counts, upload calls/bytes, startup/reload time and matched-workload GPU time. Hash ALU and dependent UBO reads may offset bandwidth savings. Retain user-run visual acceptance; do not infer performance or appearance from storage arithmetic alone.

Review this proposal and resolve its open contracts before deriving implementation subtasks. Related documents: [atlas build pipeline](MaterialAtlas.BuildPipeline.md), [transmission](PBR.Transmission.md), [baseline tasks](PBR.BaselineShading.todo).
