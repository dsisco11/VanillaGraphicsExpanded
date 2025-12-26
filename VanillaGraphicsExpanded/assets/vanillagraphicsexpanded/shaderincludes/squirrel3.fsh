/**
* Squirrel3 hash function
* https://youtu.be/LWFzPP8ZbdU?t=2674
*/
uint Squirrel3HashU(uint v1, uint v2, uint v3)
{
    const uint PRIMEU1 = 198491317U; // Large prime number with non boring bits
    const uint PRIMEU2 = 6542989U;   // Large prime number with non boring bits
    const uint PRIMEU3 = 786433U;
    const uint BIT_NOISE1 = 3039394381U;//0xB5297A4D;
    const uint BIT_NOISE2 = 1759714724U;//0x68E31DA4;
    const uint BIT_NOISE3 = 458671337U;//0x1B56C4E9;

    uint mangled = v1 + (v2 * PRIMEU1) + (v3 * PRIMEU2);
    mangled = (mangled * BIT_NOISE1);
    mangled = (mangled ^ (mangled >> 8U));
    mangled = (mangled + BIT_NOISE2);
    mangled = (mangled ^ (mangled << 8U));
    mangled = (mangled * BIT_NOISE3);
    mangled = (mangled ^ (mangled >> 8U));
    return mangled;
}

float Squirrel3HashF(vec3 p)
{
    uint x = floatBitsToUint(p.x);
    uint y = floatBitsToUint(p.y);
    uint z = floatBitsToUint(p.z);
    uint hashed = Squirrel3HashU(x, y, z);
    // Normalize uint to [0, 1] range by dividing by max uint value
    return float(hashed) / 4294967295.0;
}