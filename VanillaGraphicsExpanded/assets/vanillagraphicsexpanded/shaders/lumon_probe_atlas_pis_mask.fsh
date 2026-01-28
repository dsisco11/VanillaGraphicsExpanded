#version 330 core

// Output: probe-resolution RG32F (two uint bitfields packed via uintBitsToFloat)
layout(location = 0) out vec2 outMask;

// Import common utilities
@import "./includes/lumon_common.glsl"

// Import global defines (loop-bound knobs)
@import "./includes/vge_global_defines.glsl"

// Import UBO aliases (frameIndex, probeGridSize, etc)
@import "./includes/lumon_ubos.glsl"

// Import octahedral mapping
@import "./includes/lumon_octahedral.glsl"

// Import probe-atlas meta helpers (confidence encoding contract)
@import "./includes/lumon_probe_atlas_meta.glsl"

// Import deterministic hash
@import "./includes/squirrel3.glsl"

// Probe anchor textures (world-space)
uniform sampler2D probeAnchorPosition;  // posWS.xyz, valid
uniform sampler2D probeAnchorNormal;    // normalWS.xyz, reserved

// History atlas (octahedral-mapped) and meta history (confidence, flags)
uniform sampler2D octahedralHistory;
uniform sampler2D probeAtlasMetaHistory;

const int LUMON_TILE_TEXELS = LUMON_OCTAHEDRAL_SIZE * LUMON_OCTAHEDRAL_SIZE;

float lumonLuminance(vec3 c)
{
    return dot(c, vec3(0.2126, 0.7152, 0.0722));
}

void lumonSetMaskBit(inout uint maskLo, inout uint maskHi, int texelIndex)
{
    texelIndex = clamp(texelIndex, 0, LUMON_TILE_TEXELS - 1);
    if (texelIndex < 32)
    {
        maskLo |= (1u << uint(texelIndex));
    }
    else
    {
        maskHi |= (1u << uint(texelIndex - 32));
    }
}

bool lumonLegacyIsTracedThisFrame(int texelIndex, int probeIndex)
{
    int texelsPerFrame = max(1, VGE_LUMON_ATLAS_TEXELS_PER_FRAME);
    int numBatches = max(1, LUMON_TILE_TEXELS / texelsPerFrame);

    int batch = texelIndex / texelsPerFrame;
    int jitteredFrame = (frameIndex + probeIndex) % numBatches;
    return batch == jitteredFrame;
}

int lumonComputeExploreCount(int texelsPerFrame)
{
    int exploreCount = VGE_LUMON_PROBE_PIS_EXPLORE_COUNT;

    if (exploreCount < 0)
    {
        float kf = float(texelsPerFrame) * VGE_LUMON_PROBE_PIS_EXPLORE_FRACTION;
        exploreCount = int(floor(kf + 0.5));
    }

    exploreCount = clamp(exploreCount, 0, texelsPerFrame);
    return exploreCount;
}

void main(void)
{
    ivec2 probeCoord = ivec2(gl_FragCoord.xy);
    ivec2 probeGridSizeI = ivec2(probeGridSize);

    // Clamp to valid range (defensive; expected to match FBO size)
    probeCoord = clamp(probeCoord, ivec2(0), probeGridSizeI - 1);

    int probeIndex = probeCoord.y * probeGridSizeI.x + probeCoord.x;

    vec4 anchorData = texelFetch(probeAnchorPosition, probeCoord, 0);
    float valid = anchorData.w;
    if (valid < 0.5)
    {
        outMask = vec2(0.0);
        return;
    }

    // Debug/compatibility modes: output the legacy uniform batch slicing mask.
#if (VGE_LUMON_PROBE_PIS_FORCE_BATCH_SLICING == 1) || (VGE_LUMON_PROBE_PIS_FORCE_UNIFORM_MASK == 1) || (VGE_LUMON_PROBE_PIS_ENABLED == 0)
    {
        uint maskLo = 0u;
        uint maskHi = 0u;

        for (int i = 0; i < LUMON_TILE_TEXELS; i++)
        {
            if (lumonLegacyIsTracedThisFrame(i, probeIndex))
            {
                lumonSetMaskBit(maskLo, maskHi, i);
            }
        }

        outMask = vec2(uintBitsToFloat(maskLo), uintBitsToFloat(maskHi));
        return;
    }
#endif

    // PIS path
    uint maskLo = 0u;
    uint maskHi = 0u;

    bool selected[LUMON_TILE_TEXELS];
    for (int i = 0; i < LUMON_TILE_TEXELS; i++) selected[i] = false;

    int texelsPerFrame = max(1, VGE_LUMON_ATLAS_TEXELS_PER_FRAME);
    int exploreCount = lumonComputeExploreCount(texelsPerFrame);
    int importanceCount = max(0, texelsPerFrame - exploreCount);

    // Deterministic exploration selection based on legacy batch slicing, but cycling within the batch.
    int numBatches = max(1, LUMON_TILE_TEXELS / texelsPerFrame);
    int jitteredFrame = (frameIndex + probeIndex) % numBatches;
    int batchStart = jitteredFrame * texelsPerFrame;

    int cycle = (frameIndex + probeIndex) / numBatches;
    int withinBatchOffset = 0;
    if (exploreCount > 0)
    {
        withinBatchOffset = (cycle * exploreCount) % max(1, texelsPerFrame);
    }

    int selectedCount = 0;

    for (int j = 0; j < exploreCount; j++)
    {
        int idx = batchStart + ((withinBatchOffset + j) % max(1, texelsPerFrame));
        idx = idx % LUMON_TILE_TEXELS;

        if (!selected[idx])
        {
            selected[idx] = true;
            lumonSetMaskBit(maskLo, maskHi, idx);
            selectedCount++;
        }
    }

    // Compute importance weights and weighted-without-replacement keys.
    vec3 probeNormalWS = lumonDecodeNormal(texelFetch(probeAnchorNormal, probeCoord, 0).xyz);
    ivec2 atlasBase = probeCoord * LUMON_OCTAHEDRAL_SIZE;

    float keys[LUMON_TILE_TEXELS];
    float eps = max(1e-12, VGE_LUMON_PROBE_PIS_WEIGHT_EPSILON);
    float minConfW = clamp(VGE_LUMON_PROBE_PIS_MIN_CONFIDENCE_WEIGHT, 0.0, 1.0);

    for (int i = 0; i < LUMON_TILE_TEXELS; i++)
    {
        if (selected[i])
        {
            keys[i] = -1e30;
            continue;
        }

        ivec2 octTexel = ivec2(i % LUMON_OCTAHEDRAL_SIZE, i / LUMON_OCTAHEDRAL_SIZE);
        ivec2 atlasCoord = atlasBase + octTexel;

        vec3 historyRadiance = texelFetch(octahedralHistory, atlasCoord, 0).rgb;
        float Li = max(0.0, lumonLuminance(historyRadiance));

        float conf = texelFetch(probeAtlasMetaHistory, atlasCoord, 0).x;
        float confW = max(minConfW, conf);

        vec2 octUV = lumonTexelCoordToOctahedralUV(octTexel);
        vec3 dirWS = lumonOctahedralUVToDirection(octUV);
        float cosN = max(0.0, dot(probeNormalWS, dirWS));

        float w = Li * cosN * confW;
        if (w <= eps)
        {
            keys[i] = -1e30;
            continue;
        }

        // Efraimidis-Spirakis key method (log form): key = log(u) / w, select the K largest keys.
        float u = clamp(Squirrel3HashF(probeIndex, frameIndex, i), 1e-6, 1.0);
        keys[i] = log(u) / w;
    }

    for (int pick = 0; pick < importanceCount; pick++)
    {
        int bestIdx = -1;
        float bestKey = -1e30;

        for (int i = 0; i < LUMON_TILE_TEXELS; i++)
        {
            if (selected[i]) continue;
            float k = keys[i];
            if (k > bestKey)
            {
                bestKey = k;
                bestIdx = i;
            }
        }

        // If all remaining weights are effectively zero, stop and fall back to exploration.
        if (bestIdx < 0 || bestKey < -1e20)
        {
            break;
        }

        selected[bestIdx] = true;
        lumonSetMaskBit(maskLo, maskHi, bestIdx);
        selectedCount++;
    }

    // Fallback: if importance selection was empty (all weights ~0), fill remaining slots deterministically.
    if (selectedCount < texelsPerFrame)
    {
        for (int i = 0; i < LUMON_TILE_TEXELS && selectedCount < texelsPerFrame; i++)
        {
            // Deterministic wrap sequence anchored to the legacy batch.
            int idx = (batchStart + i) % LUMON_TILE_TEXELS;
            if (selected[idx]) continue;

            selected[idx] = true;
            lumonSetMaskBit(maskLo, maskHi, idx);
            selectedCount++;
        }
    }

    outMask = vec2(uintBitsToFloat(maskLo), uintBitsToFloat(maskHi));
}
