// Shared uniforms/samplers for LumOn debug shaders.
// Imported by the monolithic lumon_debug and by per-program-kind entrypoints.

#ifndef LUMON_DEBUG_UNIFORMS_GLSL
#define LUMON_DEBUG_UNIFORMS_GLSL

// UBO contracts (Phase 23).
@import "./lumon_ubos.glsl"

// G-buffer textures
uniform sampler2D primaryDepth;
uniform sampler2D gBufferNormal;
uniform usampler2D gBufferPatchId; // RGBA32UI: (chunkSlot, patchId, packedPatchUv, misc)

// Probe textures
uniform sampler2D probeAnchorPosition;  // posWS.xyz, valid
uniform sampler2D probeAnchorNormal;
uniform sampler2D radianceTexture0;
uniform sampler2D radianceTexture1;     // Second SH texture for full unpacking
uniform sampler2D indirectHalf;

// Temporal textures
uniform sampler2D historyMeta;          // linearized depth, normal, accumCount

// Screen-probe atlas textures
uniform sampler2D probeAtlasMeta;       // R = confidence, G = uintBitsToFloat(flags)
uniform sampler2D probeAtlasTrace;      // RGBA16F radiance atlas (pre-temporal)
uniform sampler2D probeAtlasCurrent;     // RGBA16F radiance atlas (post-temporal)
uniform sampler2D probeAtlasFiltered;    // RGBA16F radiance atlas (post-filter)
uniform sampler2D probeAtlasGatherInput; // The atlas currently selected as gather input

// Phase 10: PIS selection/mask debug inputs
uniform sampler2D probeTraceMask;        // Probe-resolution RG32F packed uint bits
uniform sampler2D probePisEnergy;        // Probe-resolution R32F importance energy (sum of weights)

// Phase 15: compositing debug inputs
uniform sampler2D indirectDiffuseFull;   // Upsampled indirect buffer (full-res)
uniform sampler2D gBufferAlbedo;         // Albedo (fallback: captured scene)
uniform sampler2D gBufferMaterial;       // Material properties (roughness/metallic/emissive/reflectivity)

// Phase 16: direct lighting debug inputs
uniform sampler2D directDiffuse;
uniform sampler2D directSpecular;
uniform sampler2D emissive;

// Phase 14: velocity buffer (RGBA32F)
uniform sampler2D velocityTex;

// Phase 18: world-probe lifecycle debug atlas (RGBA16 UNorm)
uniform sampler2D worldProbeDebugState0;

// Phase 22: LumonScene surface cache debug inputs (Near field v1).
uniform int vge_lumonSceneEnabled;                 // 0/1 gate
uniform usampler2DArray vge_lumonScenePageTableMip0;   // R32UI packed entry
uniform sampler2DArray vge_lumonSceneIrradianceAtlas;  // RGBA16F (rgb irradiance, a weight)
uniform int vge_lumonSceneTileSizeTexels;
uniform int vge_lumonSceneTilesPerAxis;
uniform int vge_lumonSceneTilesPerAtlas;

// Phase 23: TraceScene occupancy clipmap debug inputs (L0 only in v1).
uniform int vge_traceSceneEnabled;            // 0/1 gate
uniform usampler3D vge_traceOccL0;            // R32UI packed payload
uniform ivec3 vge_traceOccOriginMinCell0;
uniform ivec3 vge_traceOccRing0;
uniform int vge_traceOccResolution;

// Temporal config
uniform float temporalAlpha;
uniform float depthRejectThreshold;
uniform float normalRejectThreshold;

// Phase 14: velocity debug scaling
// (velocityRejectThreshold is provided via LumOnFrameUBO)

// Matrices for reprojection
// (invViewMatrix and prevViewProjMatrix are provided via LumOnFrameUBO)

// Debug mode (still used to select a view inside a program-kind entrypoint)
uniform int debugMode;

// Gather atlas selection: 0=trace, 1=current, 2=filtered
uniform int gatherAtlasSource;

// Phase 15: compositing parameters (to match lumon_combine behavior)
uniform float indirectIntensity;
uniform vec3 indirectTint;
uniform float diffuseAOStrength;
uniform float specularAOStrength;

#endif // LUMON_DEBUG_UNIFORMS_GLSL
