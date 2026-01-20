// ============================================================================
// LumOn World-Probe (Single Include)
//
// This file intentionally contains *all* world-probe clipmap-related glue:
// - Define fallbacks (set at compile time via VgeShaderProgram.SetDefine)
// - Shared worldProbe* uniform declarations
// - Clipmap sampling helpers + SH decode/eval
// - A bound helper that closes over the uniforms
//
// Import this from shaders as:
//   @import "./includes/lumon_worldprobe.glsl"
// ============================================================================

#ifndef LUMON_WORLDPROBE_GLSL
#define LUMON_WORLDPROBE_GLSL

// Dependencies
@import "./lumon_octahedral.glsl"
@import "./lumon_sh.glsl"

// ---------------------------------------------------------------------------
// Define fallbacks (compile-time constants)
// ---------------------------------------------------------------------------

#ifndef VGE_LUMON_WORLDPROBE_ENABLED
	#define VGE_LUMON_WORLDPROBE_ENABLED 0
#endif

#ifndef VGE_LUMON_WORLDPROBE_LEVELS
	#define VGE_LUMON_WORLDPROBE_LEVELS 0
#endif

#ifndef VGE_LUMON_WORLDPROBE_RESOLUTION
	#define VGE_LUMON_WORLDPROBE_RESOLUTION 0
#endif

#ifndef VGE_LUMON_WORLDPROBE_BASE_SPACING
	#define VGE_LUMON_WORLDPROBE_BASE_SPACING 0.0
#endif

// Expected maximum levels (matches config clamp).
#define LUMON_WORLDPROBE_MAX_LEVELS 8

// ---------------------------------------------------------------------------
// Shared uniforms
// ---------------------------------------------------------------------------

uniform sampler2D worldProbeSH0;
uniform sampler2D worldProbeSH1;
uniform sampler2D worldProbeSH2;
uniform sampler2D worldProbeVis0;
uniform sampler2D worldProbeDist0;
uniform sampler2D worldProbeMeta0;

// Camera position in the engine's camera-matrix world space (as produced by invViewMatrix reconstruction).
uniform vec3 worldProbeCameraPosWS;

// Per-level origin min-corner, stored relative to the *absolute* camera position:
//   originRel = originAbs - cameraAbs
// This keeps values small/stable in float precision while still representing a world-space anchored clipmap.
uniform vec3 worldProbeOriginMinCorner[LUMON_WORLDPROBE_MAX_LEVELS];
uniform vec3 worldProbeRingOffset[LUMON_WORLDPROBE_MAX_LEVELS];

// ---------------------------------------------------------------------------
// Clipmap sampling helpers
// ---------------------------------------------------------------------------

struct LumOnWorldProbeSample
{
	vec3 irradiance;
	float confidence;
};

float lumonWorldProbeSpacing(float baseSpacing, int level)
{
	return baseSpacing * exp2(float(level));
}

int lumonWorldProbeSelectLevelByDistance(vec3 worldPos, vec3 cameraPos, float baseSpacing, int maxLevel)
{
	float dist = length(worldPos - cameraPos);
	float ratio = max(dist, baseSpacing) / max(baseSpacing, 1e-6);
	int level = int(floor(log2(ratio)));
	return clamp(level, 0, maxLevel);
}

float lumonWorldProbeDistanceToBoundaryProbeUnits(vec3 local, int resolution)
{
	float maxI = float(resolution - 1);
	float dx = min(local.x, maxI - local.x);
	float dy = min(local.y, maxI - local.y);
	float dz = min(local.z, maxI - local.z);
	return min(dx, min(dy, dz));
}

float lumonWorldProbeCrossLevelBlendWeight(float edgeDistProbeUnits, float blendStartProbeUnits, float blendWidthProbeUnits)
{
	float t = (edgeDistProbeUnits - blendStartProbeUnits) / max(blendWidthProbeUnits, 1e-6);
	// 0 near boundary -> favor L+1; 1 inside -> favor L.
	return clamp(t, 0.0, 1.0);
}

ivec3 lumonWorldProbeWrapIndex(ivec3 idx, int resolution)
{
	// GLSL % can be negative, so implement a positive modulus.
	int rx = ((idx.x % resolution) + resolution) % resolution;
	int ry = ((idx.y % resolution) + resolution) % resolution;
	int rz = ((idx.z % resolution) + resolution) % resolution;
	return ivec3(rx, ry, rz);
}

ivec2 lumonWorldProbeAtlasCoord(ivec3 storageIndex, int level, int resolution)
{
	int u = storageIndex.x + storageIndex.z * resolution;
	int v = storageIndex.y + level * resolution;
	return ivec2(u, v);
}

void lumonWorldProbeDecodeShL1(
	vec4 t0,
	vec4 t1,
	vec4 t2,
	out vec4 shR,
	out vec4 shG,
	out vec4 shB)
{
	// Inverse of lumon_worldprobe_clipmap_resolve.fsh packing.
	shR = vec4(t0.x, t0.w, t1.z, t2.y);
	shG = vec4(t0.y, t1.x, t1.w, t2.z);
	shB = vec4(t0.z, t1.y, t2.x, t2.w);
}

void lumonWorldProbeAccumulateCorner(
	sampler2D probeSH0,
	sampler2D probeSH1,
	sampler2D probeSH2,
	sampler2D probeVis0,
	sampler2D probeMeta0,
	ivec3 localIdx,
	ivec3 ring,
	int resolution,
	int level,
	float wt,
	inout vec4 shR,
	inout vec4 shG,
	inout vec4 shB,
	inout vec3 aoDirAccum,
	inout float aoConfAccum,
	inout float metaConfAccum)
{
	ivec3 storage = lumonWorldProbeWrapIndex(localIdx + ring, resolution);
	ivec2 ac = lumonWorldProbeAtlasCoord(storage, level, resolution);

	vec4 t0 = texelFetch(probeSH0, ac, 0);
	vec4 t1 = texelFetch(probeSH1, ac, 0);
	vec4 t2 = texelFetch(probeSH2, ac, 0);

	vec4 cR, cG, cB;
	lumonWorldProbeDecodeShL1(t0, t1, t2, cR, cG, cB);

	shR += cR * wt;
	shG += cG * wt;
	shB += cB * wt;

	vec2 meta = texelFetch(probeMeta0, ac, 0).xy;
	float conf = meta.x;
	metaConfAccum += conf * wt;

	vec4 vis = texelFetch(probeVis0, ac, 0);
	vec3 aoDir = lumonOctahedralUVToDirection(vis.xy);
	aoDirAccum += aoDir * wt;
	aoConfAccum += vis.w * wt;
}

LumOnWorldProbeSample lumonWorldProbeSampleLevelTrilinear(
	sampler2D probeSH0,
	sampler2D probeSH1,
	sampler2D probeSH2,
	sampler2D probeVis0,
	sampler2D probeMeta0,
	vec3 worldPosRel,
	vec3 normalWS,
	vec3 originMinCorner,
	vec3 ringOffset,
	float spacing,
	int resolution,
	int level)
{
	LumOnWorldProbeSample s;
	s.irradiance = vec3(0.0);
	s.confidence = 0.0;

	vec3 local = (worldPosRel - originMinCorner) / max(spacing, 1e-6);

	// Outside clip volume: no contribution.
	if (any(lessThan(local, vec3(0.0))) || any(greaterThanEqual(local, vec3(float(resolution)))))
	{
		return s;
	}

	ivec3 i0 = ivec3(floor(local));
	vec3 f = fract(local);
	ivec3 i1 = i0 + ivec3(1);

	ivec3 ring = ivec3(floor(ringOffset + 0.5));

	int maxIdx = resolution - 1;

	// Clamp for safe trilinear at edges.
	if (i0.x >= maxIdx) { i0.x = maxIdx; i1.x = maxIdx; f.x = 0.0; }
	if (i0.y >= maxIdx) { i0.y = maxIdx; i1.y = maxIdx; f.y = 0.0; }
	if (i0.z >= maxIdx) { i0.z = maxIdx; i1.z = maxIdx; f.z = 0.0; }

	// Trilinear weights.
	float wx0 = 1.0 - f.x;
	float wy0 = 1.0 - f.y;
	float wz0 = 1.0 - f.z;
	float wx1 = f.x;
	float wy1 = f.y;
	float wz1 = f.z;

	float w000 = wx0 * wy0 * wz0;
	float w100 = wx1 * wy0 * wz0;
	float w010 = wx0 * wy1 * wz0;
	float w110 = wx1 * wy1 * wz0;
	float w001 = wx0 * wy0 * wz1;
	float w101 = wx1 * wy0 * wz1;
	float w011 = wx0 * wy1 * wz1;
	float w111 = wx1 * wy1 * wz1;

	vec4 shR = vec4(0.0);
	vec4 shG = vec4(0.0);
	vec4 shB = vec4(0.0);

	vec3 aoDirAccum = vec3(0.0);
	float aoConfAccum = 0.0;
	float metaConfAccum = 0.0;

	lumonWorldProbeAccumulateCorner(probeSH0, probeSH1, probeSH2, probeVis0, probeMeta0, ivec3(i0.x, i0.y, i0.z), ring, resolution, level, w000, shR, shG, shB, aoDirAccum, aoConfAccum, metaConfAccum);
	lumonWorldProbeAccumulateCorner(probeSH0, probeSH1, probeSH2, probeVis0, probeMeta0, ivec3(i1.x, i0.y, i0.z), ring, resolution, level, w100, shR, shG, shB, aoDirAccum, aoConfAccum, metaConfAccum);
	lumonWorldProbeAccumulateCorner(probeSH0, probeSH1, probeSH2, probeVis0, probeMeta0, ivec3(i0.x, i1.y, i0.z), ring, resolution, level, w010, shR, shG, shB, aoDirAccum, aoConfAccum, metaConfAccum);
	lumonWorldProbeAccumulateCorner(probeSH0, probeSH1, probeSH2, probeVis0, probeMeta0, ivec3(i1.x, i1.y, i0.z), ring, resolution, level, w110, shR, shG, shB, aoDirAccum, aoConfAccum, metaConfAccum);
	lumonWorldProbeAccumulateCorner(probeSH0, probeSH1, probeSH2, probeVis0, probeMeta0, ivec3(i0.x, i0.y, i1.z), ring, resolution, level, w001, shR, shG, shB, aoDirAccum, aoConfAccum, metaConfAccum);
	lumonWorldProbeAccumulateCorner(probeSH0, probeSH1, probeSH2, probeVis0, probeMeta0, ivec3(i1.x, i0.y, i1.z), ring, resolution, level, w101, shR, shG, shB, aoDirAccum, aoConfAccum, metaConfAccum);
	lumonWorldProbeAccumulateCorner(probeSH0, probeSH1, probeSH2, probeVis0, probeMeta0, ivec3(i0.x, i1.y, i1.z), ring, resolution, level, w011, shR, shG, shB, aoDirAccum, aoConfAccum, metaConfAccum);
	lumonWorldProbeAccumulateCorner(probeSH0, probeSH1, probeSH2, probeVis0, probeMeta0, ivec3(i1.x, i1.y, i1.z), ring, resolution, level, w111, shR, shG, shB, aoDirAccum, aoConfAccum, metaConfAccum);

	// Evaluate SH in WORLD space.
	vec3 irradiance = shEvaluateDiffuseRGB(shR, shG, shB, normalWS);

	// ShortRangeAO directional weight (leak reduction proxy).
	// Guard against undefined normalize(0) behavior on some drivers.
	vec3 aoDir = (dot(aoDirAccum, aoDirAccum) > 1e-8) ? normalize(aoDirAccum) : normalWS;
	float aoConf = clamp(aoConfAccum, 0.0, 1.0);
	float aoWeight = max(dot(normalWS, aoDir), 0.0) * aoConf;

	irradiance = max(irradiance, vec3(0.0)) * aoWeight;

	float conf = clamp(metaConfAccum, 0.0, 1.0);
	conf *= aoConf;

	s.irradiance = irradiance;
	s.confidence = conf;
	return s;
}

LumOnWorldProbeSample lumonWorldProbeSampleClipmap(
	sampler2D probeSH0,
	sampler2D probeSH1,
	sampler2D probeSH2,
	sampler2D probeVis0,
	sampler2D probeMeta0,
	vec3 worldPos,
	vec3 normalWS,
	vec3 cameraPosWS,
	vec3 originMinCorner[LUMON_WORLDPROBE_MAX_LEVELS],
	vec3 ringOffset[LUMON_WORLDPROBE_MAX_LEVELS])
{
	LumOnWorldProbeSample outS;
	outS.irradiance = vec3(0.0);
	outS.confidence = 0.0;

	const float baseSpacing = VGE_LUMON_WORLDPROBE_BASE_SPACING;
	const int levels = VGE_LUMON_WORLDPROBE_LEVELS;
	const int resolution = VGE_LUMON_WORLDPROBE_RESOLUTION;

	if (levels <= 0 || resolution <= 0)
	{
		return outS;
	}

	// Work in camera-relative space for stable float precision.
	vec3 worldPosRel = worldPos - cameraPosWS;

	int maxLevel = max(levels - 1, 0);
	int level = lumonWorldProbeSelectLevelByDistance(worldPos, cameraPosWS, baseSpacing, maxLevel);

	float spacingL = lumonWorldProbeSpacing(baseSpacing, level);
	vec3 originL = originMinCorner[level];
	vec3 ringL = ringOffset[level];

	// Cross-level overlap smoothing: blend to L+1 near boundary.
	vec3 localL = (worldPosRel - originL) / max(spacingL, 1e-6);
	float edgeDist = lumonWorldProbeDistanceToBoundaryProbeUnits(localL, resolution);
	float wL = lumonWorldProbeCrossLevelBlendWeight(edgeDist, 2.0, 2.0);

	LumOnWorldProbeSample sL = lumonWorldProbeSampleLevelTrilinear(
		probeSH0, probeSH1, probeSH2, probeVis0, probeMeta0,
		worldPosRel, normalWS,
		originL, ringL,
		spacingL, resolution, level);

	if (level < maxLevel)
	{
		int level2 = level + 1;
		float spacing2 = lumonWorldProbeSpacing(baseSpacing, level2);
		vec3 origin2 = originMinCorner[level2];
		vec3 ring2 = ringOffset[level2];

		LumOnWorldProbeSample s2 = lumonWorldProbeSampleLevelTrilinear(
			probeSH0, probeSH1, probeSH2, probeVis0, probeMeta0,
			worldPosRel, normalWS,
			origin2, ring2,
			spacing2, resolution, level2);

		outS.irradiance = sL.irradiance * wL + s2.irradiance * (1.0 - wL);
		outS.confidence = sL.confidence * wL + s2.confidence * (1.0 - wL);
		return outS;
	}

	return sL;
}

// Bound helper: closes over uniforms + compile-time defines.
LumOnWorldProbeSample lumonWorldProbeSampleClipmapBound(vec3 worldPos, vec3 normalWS)
{
	return lumonWorldProbeSampleClipmap(
		worldProbeSH0,
		worldProbeSH1,
		worldProbeSH2,
		worldProbeVis0,
		worldProbeMeta0,
		worldPos,
		normalWS,
		worldProbeCameraPosWS,
		worldProbeOriginMinCorner,
		worldProbeRingOffset);
}

#endif
