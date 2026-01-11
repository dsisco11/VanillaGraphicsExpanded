// Probe-atlas meta encoding helpers.
// Stored as RG32F:
//   R = confidence (0..1)
//   G = uintBitsToFloat(flags)

#ifndef LUMON_PROBE_ATLAS_META_GLSL
#define LUMON_PROBE_ATLAS_META_GLSL

// Bit flags
const uint LUMON_META_HIT               = 1u << 0;
const uint LUMON_META_SKY_MISS          = 1u << 1;
const uint LUMON_META_SCREEN_EXIT       = 1u << 2;
const uint LUMON_META_EARLY_TERMINATED  = 1u << 3;
const uint LUMON_META_THICKNESS_UNCERT  = 1u << 4;

vec2 lumonEncodeMeta(float confidence, uint flags)
{
    return vec2(clamp(confidence, 0.0, 1.0), uintBitsToFloat(flags));
}

void lumonDecodeMeta(vec2 meta, out float confidence, out uint flags)
{
    confidence = meta.x;
    flags = floatBitsToUint(meta.y);
}

#endif
