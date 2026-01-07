#ifndef VGE_SQUIRREL3_FSH
#define VGE_SQUIRREL3_FSH
/**
* Squirrel3 hash function
* https://youtu.be/LWFzPP8ZbdU?t=2674
*/

const uint SQUIRREL3_PRIMEU1 = 198491317U; // Large prime number with non boring bits
const uint SQUIRREL3_PRIMEU2 = 6542989U;   // Large prime number with non boring bits
const uint SQUIRREL3_PRIMEU3 = 786433U;
const uint SQUIRREL3_BIT_NOISE1 = 3039394381U;//0xB5297A4D;
const uint SQUIRREL3_BIT_NOISE2 = 1759714724U;//0x68E31DA4;
const uint SQUIRREL3_BIT_NOISE3 = 458671337U;//0x1B56C4E9;
const float SQUIRREL3_FLOAT_MAX = 4294967295.0; // Max value for normalization

// #region UInt Hash Functions

/**
 * Squirrel3 hash function for one uint input.
 * @returns 32-bit hashed uint value
 */
uint Squirrel3HashU(uint v1)
{
    uint mangled = v1;
    mangled = (mangled * SQUIRREL3_BIT_NOISE1);
    mangled = (mangled ^ (mangled >> 8U));
    return mangled;
}

/**
 * Squirrel3 hash function for two uint inputs.
 * @returns 32-bit hashed uint value
 */
uint Squirrel3HashU(uint v1, uint v2)
{
    uint mangled = v1 + (v2 * SQUIRREL3_PRIMEU1);
    mangled = (mangled * SQUIRREL3_BIT_NOISE1);
    mangled = (mangled ^ (mangled >> 8U));
    mangled = (mangled + SQUIRREL3_BIT_NOISE2);
    mangled = (mangled ^ (mangled << 8U));
    return mangled;
}

/**
 * Squirrel3 hash function for three uint inputs.
 * @returns 32-bit hashed uint value
 */
uint Squirrel3HashU(uint v1, uint v2, uint v3)
{
    uint mangled = v1 + (v2 * SQUIRREL3_PRIMEU1) + (v3 * SQUIRREL3_PRIMEU2);
    mangled = (mangled * SQUIRREL3_BIT_NOISE1);
    mangled = (mangled ^ (mangled >> 8U));
    mangled = (mangled + SQUIRREL3_BIT_NOISE2);
    mangled = (mangled ^ (mangled << 8U));
    mangled = (mangled * SQUIRREL3_BIT_NOISE3);
    mangled = (mangled ^ (mangled >> 8U));
    return mangled;
}
// #endregion

// #region Float Hash Functions

/**
 * Squirrel3 hash function for a single uint seed.
 * @returns Returns a float in [0, 1] range.
 */
float Squirrel3HashF(uint seed)
{
    uint hashed = Squirrel3HashU(seed, 0U, 0U);
    return float(hashed) / SQUIRREL3_FLOAT_MAX;
}

/**
 * Squirrel3 hash function for two uint inputs.
 * @returns Returns a float in [0, 1] range.
 */
float Squirrel3HashF(uint v1, uint v2)
{
    uint hashed = Squirrel3HashU(v1, v2);
    return float(hashed) / SQUIRREL3_FLOAT_MAX;
}

/**
 * Squirrel3 hash function for three uint inputs.
 * @returns Returns a float in [0, 1] range.
 */
float Squirrel3HashF(uint v1, uint v2, uint v3)
{
    uint hashed = Squirrel3HashU(v1, v2, v3);
    return float(hashed) / SQUIRREL3_FLOAT_MAX;
}

float Squirrel3HashF(int v1, int v2, int v3)
{
    uint uv1 = uint(v1);
    uint uv2 = uint(v2);
    uint uv3 = uint(v3);
    uint hashed = Squirrel3HashU(uv1, uv2, uv3);
    return float(hashed) / SQUIRREL3_FLOAT_MAX;
}

/**
 * Squirrel3 hash function for a float input.
 * @returns Returns a float in [0, 1] range.
 */
float Squirrel3HashF(float p)
{
    uint x = floatBitsToUint(p);
    uint hashed = Squirrel3HashU(x);
    // Normalize uint to [0, 1] range by dividing by max uint value
    return float(hashed) / SQUIRREL3_FLOAT_MAX;
}

/**
 * Squirrel3 hash function for a vec2 input.
 * @returns Returns a float in [0, 1] range.
 */
float Squirrel3HashF(vec2 p)
{
    uint x = floatBitsToUint(p.x);
    uint y = floatBitsToUint(p.y);
    uint hashed = Squirrel3HashU(x, y);
    // Normalize uint to [0, 1] range by dividing by max uint value
    return float(hashed) / SQUIRREL3_FLOAT_MAX;
}

/**
 * Squirrel3 hash function for a vec3 input.
 * @returns Returns a float in [0, 1] range.
 */
float Squirrel3HashF(vec3 p)
{
    uint x = floatBitsToUint(p.x);
    uint y = floatBitsToUint(p.y);
    uint z = floatBitsToUint(p.z);
    uint hashed = Squirrel3HashU(x, y, z);
    // Normalize uint to [0, 1] range by dividing by max uint value
    return float(hashed) / SQUIRREL3_FLOAT_MAX;
}

// #endregion
#endif // VGE_SQUIRREL3_FSH