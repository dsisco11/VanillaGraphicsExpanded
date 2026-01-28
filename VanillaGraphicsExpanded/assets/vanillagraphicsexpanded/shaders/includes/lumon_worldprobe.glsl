// ============================================================================
// LumOn World-Probe (Single Include)
//
// This file intentionally contains *all* world-probe clipmap-related glue:
// - Define fallbacks (set at compile time via VgeShaderProgram.SetDefine)
// - Shared worldProbe* uniform declarations
// - Clipmap sampling helpers + radiance atlas sampling
// - A bound helper that closes over the uniforms
//
// Import this from shaders as:
//   @import "./includes/lumon_worldprobe.glsl"
// ============================================================================

#ifndef LUMON_WORLDPROBE_GLSL
#define LUMON_WORLDPROBE_GLSL

// Dependencies
@import "./lumon_common.glsl"
@import "./lumon_octahedral.glsl"
@import "./lumon_worldprobe_atlas.glsl"

// Optional UBO contracts (Phase 23).
@import "./lumon_ubos.glsl"

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
#ifndef LUMON_WORLDPROBE_MAX_LEVELS
	#define LUMON_WORLDPROBE_MAX_LEVELS 8
#endif

// ---------------------------------------------------------------------------
// Shared uniforms
// ---------------------------------------------------------------------------

// World-probe radiance atlas (RGBA16F): RGB=radiance, A=signed log(dist+1)
uniform sampler2D worldProbeRadianceAtlas;
uniform sampler2D worldProbeVis0;
uniform sampler2D worldProbeDist0;
uniform sampler2D worldProbeMeta0;

vec3 lumonWorldProbeGetSkyTint() { return lumonWorldProbe.worldProbeSkyTint.xyz; }
vec3 lumonWorldProbeGetCameraPosWS() { return lumonWorldProbe.worldProbeCameraPosWS.xyz; }
vec3 lumonWorldProbeGetOriginMinCorner(int level) { return lumonWorldProbe.worldProbeOriginMinCorner[level].xyz; }
vec3 lumonWorldProbeGetRingOffset(int level) { return lumonWorldProbe.worldProbeRingOffset[level].xyz; }

// ---------------------------------------------------------------------------
// Clipmap sampling helpers
// ---------------------------------------------------------------------------

struct LumOnWorldProbeSample
{
	vec3 irradiance;
	float confidence;
};

struct LumOnWorldProbeRadianceSample
{
	vec3 radiance;
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

bool lumonWorldProbeIsInsideLevel(vec3 worldPosRel, vec3 originMinCorner, float spacing, int resolution)
{
	vec3 local = (worldPosRel - originMinCorner) / max(spacing, 1e-6);
	return !any(lessThan(local, vec3(0.0))) && !any(greaterThanEqual(local, vec3(float(resolution))));
}

int lumonWorldProbeSelectLevelByExtents(
	vec3 worldPosRel,
	float baseSpacing,
	int levels,
	int resolution)
{
	int maxLevel = max(levels - 1, 0);

	for (int level = 0; level < maxLevel; level++)
	{
		float spacing = lumonWorldProbeSpacing(baseSpacing, level);
		if (lumonWorldProbeIsInsideLevel(worldPosRel, lumonWorldProbeGetOriginMinCorner(level), spacing, resolution))
		{
			return level;
		}
	}

	return maxLevel;
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

// ---------------------------------------------------------------------------
// Radiance atlas helpers (octahedral tiles)
// ---------------------------------------------------------------------------

vec2 lumonWorldProbeTexelCoordToOctahedralUV(ivec2 texelCoord)
{
	return (vec2(texelCoord) + 0.5) / LUMON_WORLDPROBE_OCTAHEDRAL_SIZE_F;
}

ivec2 lumonWorldProbeDirectionToOctahedralTexel(vec3 dir)
{
	vec2 uv = lumonDirectionToOctahedralUV(normalize(dir));
	ivec2 texel = ivec2(uv * LUMON_WORLDPROBE_OCTAHEDRAL_SIZE_F);
	texel = clamp(texel, ivec2(0), ivec2(LUMON_WORLDPROBE_OCTAHEDRAL_SIZE_I - 1));
	return texel;
}

ivec2 lumonWorldProbeRadianceAtlasTileOrigin(ivec3 storageIndex, int level, int resolution)
{
	int s = LUMON_WORLDPROBE_OCTAHEDRAL_SIZE_I;
	int tileU0 = (storageIndex.x + storageIndex.z * resolution) * s;
	int tileV0 = (storageIndex.y + level * resolution) * s;
	return ivec2(tileU0, tileV0);
}

vec4 lumonWorldProbeFetchRadianceAtlasTexel(
	sampler2D radianceAtlas,
	ivec3 storageIndex,
	int level,
	int resolution,
	ivec2 octTexel)
{
	ivec2 tile0 = lumonWorldProbeRadianceAtlasTileOrigin(storageIndex, level, resolution);
	ivec2 atlasTexel = tile0 + octTexel;
	return texelFetch(radianceAtlas, atlasTexel, 0);
}

bool lumonWorldProbeIsSkyVisible(float alphaSigned)
{
	return alphaSigned < 0.0;
}

float lumonWorldProbeDecodeHitDistanceSigned(float alphaSigned)
{
	return exp(abs(alphaSigned)) - 1.0;
}

void lumonWorldProbeAccumulateCornerScalars(
	sampler2D probeVis0,
	sampler2D probeMeta0,
	ivec3 localIdx,
	ivec3 ring,
	int resolution,
	int level,
	float wt,
	out ivec3 outStorage,
	out float outW,
	inout float metaConfAccum,
	inout vec3 aoDirAccum,
	inout float aoConfAccum,
	inout float skyIntensityAccum)
{
	outStorage = lumonWorldProbeWrapIndex(localIdx + ring, resolution);
	ivec2 ac = lumonWorldProbeAtlasCoord(outStorage, level, resolution);

	vec2 meta = texelFetch(probeMeta0, ac, 0).xy;
	float conf = clamp(meta.x, 0.0, 1.0);

	outW = wt * conf;
	metaConfAccum += outW;
	if (outW <= 0.0)
	{
		return;
	}

	vec4 vis = texelFetch(probeVis0, ac, 0);
	vec3 aoDir = lumonOctahedralUVToDirection(vis.xy);
	aoDirAccum += aoDir * outW;
	skyIntensityAccum += clamp(vis.z, 0.0, 1.0) * outW;
	aoConfAccum += clamp(vis.w, 0.0, 1.0) * outW;
}

LumOnWorldProbeSample lumonWorldProbeSampleLevelTrilinear(
	sampler2D probeRadianceAtlas,
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

	// Probe centers are at cell-centers:
	//   probe(i) center = originMinCorner + (i + 0.5) * spacing
	// Convert to "probe index space" where centers are at integer coordinates.
	vec3 localCell = (worldPosRel - originMinCorner) / max(spacing, 1e-6);

	// Outside clip volume: no contribution.
	if (any(lessThan(localCell, vec3(0.0))) || any(greaterThanEqual(localCell, vec3(float(resolution)))))
	{
		return s;
	}

	int maxIdx = resolution - 1;
	vec3 localProbe = clamp(localCell - vec3(0.5), vec3(0.0), vec3(float(maxIdx)));

	ivec3 i0 = ivec3(floor(localProbe));
	vec3 f = fract(localProbe);
	ivec3 i1 = min(i0 + ivec3(1), ivec3(maxIdx));

	ivec3 ring = ivec3(floor(ringOffset + 0.5));

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

	float skyIntensityAccum = 0.0;
	vec3 aoDirAccum = vec3(0.0);
	float aoConfAccum = 0.0;
	float metaConfAccum = 0.0;

	ivec3 cornerStorage[8];
	float cornerW[8];

	lumonWorldProbeAccumulateCornerScalars(probeVis0, probeMeta0, ivec3(i0.x, i0.y, i0.z), ring, resolution, level, w000, cornerStorage[0], cornerW[0], metaConfAccum, aoDirAccum, aoConfAccum, skyIntensityAccum);
	lumonWorldProbeAccumulateCornerScalars(probeVis0, probeMeta0, ivec3(i1.x, i0.y, i0.z), ring, resolution, level, w100, cornerStorage[1], cornerW[1], metaConfAccum, aoDirAccum, aoConfAccum, skyIntensityAccum);
	lumonWorldProbeAccumulateCornerScalars(probeVis0, probeMeta0, ivec3(i0.x, i1.y, i0.z), ring, resolution, level, w010, cornerStorage[2], cornerW[2], metaConfAccum, aoDirAccum, aoConfAccum, skyIntensityAccum);
	lumonWorldProbeAccumulateCornerScalars(probeVis0, probeMeta0, ivec3(i1.x, i1.y, i0.z), ring, resolution, level, w110, cornerStorage[3], cornerW[3], metaConfAccum, aoDirAccum, aoConfAccum, skyIntensityAccum);
	lumonWorldProbeAccumulateCornerScalars(probeVis0, probeMeta0, ivec3(i0.x, i0.y, i1.z), ring, resolution, level, w001, cornerStorage[4], cornerW[4], metaConfAccum, aoDirAccum, aoConfAccum, skyIntensityAccum);
	lumonWorldProbeAccumulateCornerScalars(probeVis0, probeMeta0, ivec3(i1.x, i0.y, i1.z), ring, resolution, level, w101, cornerStorage[5], cornerW[5], metaConfAccum, aoDirAccum, aoConfAccum, skyIntensityAccum);
	lumonWorldProbeAccumulateCornerScalars(probeVis0, probeMeta0, ivec3(i0.x, i1.y, i1.z), ring, resolution, level, w011, cornerStorage[6], cornerW[6], metaConfAccum, aoDirAccum, aoConfAccum, skyIntensityAccum);
	lumonWorldProbeAccumulateCornerScalars(probeVis0, probeMeta0, ivec3(i1.x, i1.y, i1.z), ring, resolution, level, w111, cornerStorage[7], cornerW[7], metaConfAccum, aoDirAccum, aoConfAccum, skyIntensityAccum);

	if (metaConfAccum <= 1e-6)
	{
		return s;
	}

	float invW = 1.0 / metaConfAccum;
	skyIntensityAccum *= invW;
	aoDirAccum *= invW;
	aoConfAccum *= invW;

	// ShortRangeAO: use a bent-normal evaluation rather than a hard directional multiplier.
	// The old multiplier (max(dot(n, aoDir),0) * aoConf) can go to zero for side faces outdoors
	// (aoDir tends to be "up"), which incorrectly kills world-probe contribution on walls.
	//
	// Instead, evaluate irradiance in a bent direction and use AO confidence only as the bend amount.
	// Guard against undefined normalize(0) behavior on some drivers.
	vec3 aoDir = (dot(aoDirAccum, aoDirAccum) > 1e-8) ? normalize(aoDirAccum) : normalWS;
	float aoConf = clamp(aoConfAccum, 0.0, 1.0);
	vec3 bentNormalWS = (aoConf > 1e-6)
		? normalize(mix(normalWS, aoDir, aoConf))
		: normalWS;

	// Diffuse integration over the radiance atlas. We sample all SxS directions with a stride
	// to bound gather cost.
#ifndef VGE_LUMON_WORLDPROBE_DIFFUSE_STRIDE
	#define VGE_LUMON_WORLDPROBE_DIFFUSE_STRIDE 2
#endif
	int stride = max(1, VGE_LUMON_WORLDPROBE_DIFFUSE_STRIDE);

	vec3 sumBlock = vec3(0.0);
	float sumSkyVis = 0.0;
	float sumCos = 0.0;

	for (int oy = 0; oy < LUMON_WORLDPROBE_OCTAHEDRAL_SIZE_I; oy += stride)
	{
		for (int ox = 0; ox < LUMON_WORLDPROBE_OCTAHEDRAL_SIZE_I; ox += stride)
		{
			ivec2 octTexel = ivec2(ox, oy);
			vec2 octUV = lumonWorldProbeTexelCoordToOctahedralUV(octTexel);
			vec3 dirWS = lumonOctahedralUVToDirection(octUV);

			float cw = max(dot(dirWS, bentNormalWS), 0.0);
			if (cw <= 1e-6) continue;
			sumCos += cw;

			vec3 blockDirAccum = vec3(0.0);
			float skyVisAccum = 0.0;

			for (int c = 0; c < 8; c++)
			{
				float w = cornerW[c];
				if (w <= 0.0) continue;

				vec4 t = lumonWorldProbeFetchRadianceAtlasTexel(
					probeRadianceAtlas,
					cornerStorage[c],
					level,
					resolution,
					octTexel);

				if (lumonWorldProbeIsSkyVisible(t.a))
				{
					skyVisAccum += w;
				}
				else
				{
					blockDirAccum += max(t.rgb, vec3(0.0)) * w;
				}
			}

			sumBlock += (blockDirAccum * invW) * cw;
			sumSkyVis += (skyVisAccum * invW) * cw;
		}
	}

	float skyIntensity = clamp(skyIntensityAccum, 0.0, 1.0);
	vec3 skyTint = max(lumonWorldProbeGetSkyTint(), vec3(0.0));

	vec3 irradiance = vec3(0.0);
	if (sumCos > 1e-6)
	{
		vec3 avgBlock = sumBlock / sumCos;
		float avgSkyVis = sumSkyVis / sumCos;
		irradiance = avgBlock * LUMON_PI + skyTint * (skyIntensity * avgSkyVis * LUMON_PI);
		irradiance = max(irradiance, vec3(0.0));
	}

	// ShortRangeAO is a leak-reduction factor applied to irradiance only; it should not 
	// tank confidence, otherwise world-probes get blended out in enclosed spaces.
	float conf = clamp(metaConfAccum, 0.0, 1.0);

	s.irradiance = irradiance;
	s.confidence = conf;
	return s;
}

LumOnWorldProbeRadianceSample lumonWorldProbeSampleLevelTrilinearRadiance(
	sampler2D probeRadianceAtlas,
	sampler2D probeVis0,
	sampler2D probeMeta0,
	vec3 worldPosRel,
	vec3 dirWS,
	vec3 originMinCorner,
	vec3 ringOffset,
	float spacing,
	int resolution,
	int level)
{
	LumOnWorldProbeRadianceSample s;
	s.radiance = vec3(0.0);
	s.confidence = 0.0;

	// Probe centers are at cell-centers (see lumonWorldProbeSampleLevelTrilinear).
	vec3 localCell = (worldPosRel - originMinCorner) / max(spacing, 1e-6);

	// Outside clip volume: no contribution.
	if (any(lessThan(localCell, vec3(0.0))) || any(greaterThanEqual(localCell, vec3(float(resolution)))))
	{
		return s;
	}

	int maxIdx = resolution - 1;
	vec3 localProbe = clamp(localCell - vec3(0.5), vec3(0.0), vec3(float(maxIdx)));

	ivec3 i0 = ivec3(floor(localProbe));
	vec3 f = fract(localProbe);
	ivec3 i1 = min(i0 + ivec3(1), ivec3(maxIdx));

	ivec3 ring = ivec3(floor(ringOffset + 0.5));

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

	float skyIntensityAccum = 0.0;
	vec3 aoDirAccum = vec3(0.0);
	float aoConfAccum = 0.0;
	float metaConfAccum = 0.0;

	ivec3 cornerStorage[8];
	float cornerW[8];

	lumonWorldProbeAccumulateCornerScalars(probeVis0, probeMeta0, ivec3(i0.x, i0.y, i0.z), ring, resolution, level, w000, cornerStorage[0], cornerW[0], metaConfAccum, aoDirAccum, aoConfAccum, skyIntensityAccum);
	lumonWorldProbeAccumulateCornerScalars(probeVis0, probeMeta0, ivec3(i1.x, i0.y, i0.z), ring, resolution, level, w100, cornerStorage[1], cornerW[1], metaConfAccum, aoDirAccum, aoConfAccum, skyIntensityAccum);
	lumonWorldProbeAccumulateCornerScalars(probeVis0, probeMeta0, ivec3(i0.x, i1.y, i0.z), ring, resolution, level, w010, cornerStorage[2], cornerW[2], metaConfAccum, aoDirAccum, aoConfAccum, skyIntensityAccum);
	lumonWorldProbeAccumulateCornerScalars(probeVis0, probeMeta0, ivec3(i1.x, i1.y, i0.z), ring, resolution, level, w110, cornerStorage[3], cornerW[3], metaConfAccum, aoDirAccum, aoConfAccum, skyIntensityAccum);
	lumonWorldProbeAccumulateCornerScalars(probeVis0, probeMeta0, ivec3(i0.x, i0.y, i1.z), ring, resolution, level, w001, cornerStorage[4], cornerW[4], metaConfAccum, aoDirAccum, aoConfAccum, skyIntensityAccum);
	lumonWorldProbeAccumulateCornerScalars(probeVis0, probeMeta0, ivec3(i1.x, i0.y, i1.z), ring, resolution, level, w101, cornerStorage[5], cornerW[5], metaConfAccum, aoDirAccum, aoConfAccum, skyIntensityAccum);
	lumonWorldProbeAccumulateCornerScalars(probeVis0, probeMeta0, ivec3(i0.x, i1.y, i1.z), ring, resolution, level, w011, cornerStorage[6], cornerW[6], metaConfAccum, aoDirAccum, aoConfAccum, skyIntensityAccum);
	lumonWorldProbeAccumulateCornerScalars(probeVis0, probeMeta0, ivec3(i1.x, i1.y, i1.z), ring, resolution, level, w111, cornerStorage[7], cornerW[7], metaConfAccum, aoDirAccum, aoConfAccum, skyIntensityAccum);

	if (metaConfAccum <= 1e-6)
	{
		return s;
	}

	float invW = 1.0 / metaConfAccum;
	skyIntensityAccum *= invW;
	aoDirAccum *= invW;
	aoConfAccum *= invW;

	ivec2 octTexel = lumonWorldProbeDirectionToOctahedralTexel(dirWS);

	vec3 blockDirAccum = vec3(0.0);
	float skyVisAccum = 0.0;

	for (int c = 0; c < 8; c++)
	{
		float w = cornerW[c];
		if (w <= 0.0) continue;

		vec4 t = lumonWorldProbeFetchRadianceAtlasTexel(
			probeRadianceAtlas,
			cornerStorage[c],
			level,
			resolution,
			octTexel);

		if (lumonWorldProbeIsSkyVisible(t.a))
		{
			skyVisAccum += w;
		}
		else
		{
			blockDirAccum += max(t.rgb, vec3(0.0)) * w;
		}
	}

	float skyIntensity = clamp(skyIntensityAccum, 0.0, 1.0);
	vec3 skyTint = max(lumonWorldProbeGetSkyTint(), vec3(0.0));

	vec3 radianceBlock = blockDirAccum * invW;
	float skyVis = skyVisAccum * invW;
	vec3 radiance = radianceBlock + skyTint * (skyIntensity * skyVis);
	radiance = max(radiance, vec3(0.0));

	float conf = clamp(metaConfAccum, 0.0, 1.0);

	s.radiance = radiance;
	s.confidence = conf;
	return s;
}

LumOnWorldProbeRadianceSample lumonWorldProbeSampleClipmapRadiance(
	sampler2D probeRadianceAtlas,
	sampler2D probeVis0,
	sampler2D probeMeta0,
	vec3 worldPos,
	vec3 dirWS)
{
	LumOnWorldProbeRadianceSample outS;
	outS.radiance = vec3(0.0);
	outS.confidence = 0.0;

	const float baseSpacing = VGE_LUMON_WORLDPROBE_BASE_SPACING;
	const int levels = VGE_LUMON_WORLDPROBE_LEVELS;
	const int resolution = VGE_LUMON_WORLDPROBE_RESOLUTION;

	if (levels <= 0 || resolution <= 0)
	{
		return outS;
	}

	// Work in camera-relative space for stable float precision.
	vec3 cameraPosWS = lumonWorldProbeGetCameraPosWS();
	vec3 worldPosRel = worldPos - cameraPosWS;

	int maxLevel = max(levels - 1, 0);
	int level = lumonWorldProbeSelectLevelByExtents(worldPosRel, baseSpacing, levels, resolution);

	float spacingL = lumonWorldProbeSpacing(baseSpacing, level);
	vec3 originL = lumonWorldProbeGetOriginMinCorner(level);
	vec3 ringL = lumonWorldProbeGetRingOffset(level);

	// Cross-level overlap smoothing: blend to L+1 near boundary.
	vec3 localL = (worldPosRel - originL) / max(spacingL, 1e-6);
	float edgeDist = lumonWorldProbeDistanceToBoundaryProbeUnits(localL, resolution);
	float wL = lumonWorldProbeCrossLevelBlendWeight(edgeDist, 2.0, 2.0);

	LumOnWorldProbeRadianceSample sL = lumonWorldProbeSampleLevelTrilinearRadiance(
		probeRadianceAtlas, probeVis0, probeMeta0,
		worldPosRel, dirWS,
		originL, ringL,
		spacingL, resolution, level);

	if (level < maxLevel)
	{
		int level2 = level + 1;
		float spacing2 = lumonWorldProbeSpacing(baseSpacing, level2);
		vec3 origin2 = lumonWorldProbeGetOriginMinCorner(level2);
		vec3 ring2 = lumonWorldProbeGetRingOffset(level2);

		LumOnWorldProbeRadianceSample s2 = lumonWorldProbeSampleLevelTrilinearRadiance(
			probeRadianceAtlas, probeVis0, probeMeta0,
			worldPosRel, dirWS,
			origin2, ring2,
			spacing2, resolution, level2);

		// Hole-tolerant fallback: if the fine level has no valid probes nearby (common near terrain,
		// where L0 probe centers can be disabled due to solid collision), fall back to coarser data.
		const float holeThreshold = 0.05;
		float holeW = clamp((holeThreshold - sL.confidence) / holeThreshold, 0.0, 1.0);

		float coarseW = max(1.0 - wL, holeW);
		float fineW = 1.0 - coarseW;

		outS.radiance = sL.radiance * fineW + s2.radiance * coarseW;
		outS.confidence = sL.confidence * fineW + s2.confidence * coarseW;
		return outS;
	}

	return sL;
}

LumOnWorldProbeRadianceSample lumonWorldProbeSampleClipmapRadianceBound(vec3 worldPos, vec3 dirWS)
{
	return lumonWorldProbeSampleClipmapRadiance(
		worldProbeRadianceAtlas,
		worldProbeVis0,
		worldProbeMeta0,
		worldPos,
		dirWS);
}

LumOnWorldProbeSample lumonWorldProbeSampleClipmap(
	sampler2D probeRadianceAtlas,
	sampler2D probeVis0,
	sampler2D probeMeta0,
	vec3 worldPos,
	vec3 normalWS)
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
	vec3 cameraPosWS = lumonWorldProbeGetCameraPosWS();
	vec3 worldPosRel = worldPos - cameraPosWS;

	int maxLevel = max(levels - 1, 0);
	int level = lumonWorldProbeSelectLevelByExtents(worldPosRel, baseSpacing, levels, resolution);

	float spacingL = lumonWorldProbeSpacing(baseSpacing, level);
	vec3 originL = lumonWorldProbeGetOriginMinCorner(level);
	vec3 ringL = lumonWorldProbeGetRingOffset(level);

	// Cross-level overlap smoothing: blend to L+1 near boundary.
	vec3 localL = (worldPosRel - originL) / max(spacingL, 1e-6);
	float edgeDist = lumonWorldProbeDistanceToBoundaryProbeUnits(localL, resolution);
	float wL = lumonWorldProbeCrossLevelBlendWeight(edgeDist, 2.0, 2.0);

	LumOnWorldProbeSample sL = lumonWorldProbeSampleLevelTrilinear(
		probeRadianceAtlas, probeVis0, probeMeta0,
		worldPosRel, normalWS,
		originL, ringL,
		spacingL, resolution, level);

	if (level < maxLevel)
	{
		int level2 = level + 1;
		float spacing2 = lumonWorldProbeSpacing(baseSpacing, level2);
		vec3 origin2 = lumonWorldProbeGetOriginMinCorner(level2);
		vec3 ring2 = lumonWorldProbeGetRingOffset(level2);

		LumOnWorldProbeSample s2 = lumonWorldProbeSampleLevelTrilinear(
			probeRadianceAtlas, probeVis0, probeMeta0,
			worldPosRel, normalWS,
			origin2, ring2,
			spacing2, resolution, level2);

		// Hole-tolerant fallback: if the fine level has no valid probes nearby (common near terrain,
		// where L0 probe centers can be disabled due to solid collision), fall back to coarser data.
		const float holeThreshold = 0.05;
		float holeW = clamp((holeThreshold - sL.confidence) / holeThreshold, 0.0, 1.0);

		float coarseW = max(1.0 - wL, holeW);
		float fineW = 1.0 - coarseW;

		outS.irradiance = sL.irradiance * fineW + s2.irradiance * coarseW;
		outS.confidence = sL.confidence * fineW + s2.confidence * coarseW;
		return outS;
	}

	return sL;
}

// Bound helper: closes over uniforms + compile-time defines.
LumOnWorldProbeSample lumonWorldProbeSampleClipmapBound(vec3 worldPos, vec3 normalWS)
{
	return lumonWorldProbeSampleClipmap(
		worldProbeRadianceAtlas,
		worldProbeVis0,
		worldProbeMeta0,
		worldPos,
		normalWS);
}

#endif
