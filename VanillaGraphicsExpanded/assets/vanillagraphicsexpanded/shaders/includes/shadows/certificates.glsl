#ifndef VGE_SHADOW_CERTIFICATES
#define VGE_SHADOW_CERTIFICATES
/// Compares unsigned low/high clock or identity pairs with baseline integer operations.
bool shadowPairLeq(uvec2 a,uvec2 b) { return a.y<b.y || (a.y==b.y && a.x<=b.x); }
/// Subtracts ordered clock samples before converting a duration to floating point.
uvec2 shadowTicksSubtract(uvec2 a,uvec2 b) { return uvec2(a.x-b.x,a.y-b.y-uint(a.x<b.x)); }
/// Converts an elapsed integer duration to a diagnostic value after epoch subtraction.
float shadowTickSeconds(uvec2 ticks) { return float(ticks.y)*4.294967296+float(ticks.x)*1e-9; }
/// Validates a canonical chunk identity plus bounded local FP32 endpoint.
bool shadowEndpointValid(uvec2 p) {
    float local=uintBitsToFloat(p.y);
    return !isnan(local) && !isinf(local) && local>=0.0 &&
        (local<32.0 || (int(p.x)==2147483647 && local==32.0));
}
/// Compares local endpoints without converting global integer partition identities to floats.
bool shadowEndpointLeq(uvec2 a,uvec2 b) {
    return int(a.x)<int(b.x) || (a.x==b.x && uintBitsToFloat(a.y)<=uintBitsToFloat(b.y));
}
/// Checks an opaque CPU projection scalar without performing GPU FP64 arithmetic.
bool shadowScalarFinite(uvec2 p) { return (p.y&0x7ff00000u)!=0x7ff00000u; }
/// Checks ordering of CPU projection metadata, including negative depths and signed zero.
bool shadowScalarLess(uvec2 a,uvec2 b) {
    if((a.y&0x7fffffffu)==0u && a.x==0u && (b.y&0x7fffffffu)==0u && b.x==0u) return false;
    uvec2 x=(a.y&0x80000000u)!=0u?~a:uvec2(a.x,a.y^0x80000000u);
    uvec2 y=(b.y&0x80000000u)!=0u?~b:uvec2(b.x,b.y^0x80000000u);
    return !all(equal(x,y)) && shadowPairLeq(x,y);
}
/// Subtracts signed chunk identities exactly before local FP32 geometry calculations.
float shadowCellDelta(int cell,int origin) {
    return cell>=origin?float(uint(cell)-uint(origin)):-float(uint(origin)-uint(cell));
}
#endif
