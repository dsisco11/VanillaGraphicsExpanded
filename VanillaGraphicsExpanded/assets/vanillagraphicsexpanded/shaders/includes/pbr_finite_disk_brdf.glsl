#ifndef VGE_PBR_FINITE_DISK_BRDF_GLSL
#define VGE_PBR_FINITE_DISK_BRDF_GLSL
@import "./pbr_common.glsl"

// Matches the existing visible solar emitter's half-angle in radians.
const float VGE_SOLAR_ANGULAR_RADIUS = 0.021377339;

/** Selects a direction within the circular emitter cone that maximizes GGX half-vector alignment. */
vec3 VgeFiniteDiskRepresentativeDirection(vec3 N, vec3 V, vec3 L, float sineRadius)
{
    float cosineRadius = sqrt(max(0.0, 1.0 - sineRadius * sineRadius));
    vec3 reflection = reflect(-V, N);
    float alignment = clamp(dot(reflection, L), -1.0, 1.0);
    if (alignment >= cosineRadius) return reflection;

    // Outside the disk, the optimum lies on its circular boundary. Start toward the
    // mirror direction, then refine the half-vector objective along that boundary.
    vec3 tangent = reflection - alignment * L;
    float tangentSquared = dot(tangent, tangent);
    if (tangentSquared <= 1e-12) return L;
    tangent *= inversesqrt(tangentSquared);
    vec3 bitangent = cross(L, tangent);
    float n0 = dot(N, V) + cosineRadius * dot(N, L);
    float nc = sineRadius * dot(N, tangent);
    float ns = sineRadius * dot(N, bitangent);
    float d0 = 1.0 + cosineRadius * dot(V, L);
    float dc = sineRadius * dot(V, tangent);
    float ds = sineRadius * dot(V, bitangent);
    float angle = 0.0;
    for (int iteration = 0; iteration < 4; ++iteration)
    {
        float c = cos(angle), s = sin(angle);
        float n = n0 + nc * c + ns * s;
        float d = d0 + dc * c + ds * s;
        float np = -nc * s + ns * c;
        float dp = -dc * s + ds * c;
        float npp = -nc * c - ns * s;
        float dpp = -dc * c - ds * s;
        // Stationarity of n/sqrt(2*d): 2*n'*d - n*d' = 0.
        float derivative = 2.0 * np * d - n * dp;
        float curvature = 2.0 * npp * d + np * dp - n * dpp;
        if (abs(curvature) <= 1e-10) break;
        angle -= clamp(derivative / curvature, -0.5, 0.5);
    }
    vec3 selected = cosineRadius * L + sineRadius * (cos(angle) * tangent + sin(angle) * bitangent);
    vec3 selectedHalf = V + selected;
    vec3 centerHalf = V + L;
    float selectedAlignment = dot(N, selectedHalf) * inversesqrt(max(dot(selectedHalf, selectedHalf), 1e-12));
    float centerAlignment = dot(N, centerHalf) * inversesqrt(max(dot(centerHalf, centerHalf), 1e-12));
    return selectedAlignment >= centerAlignment ? selected : L;
}

/** Approximates finite-disk specular response, retaining center-direction masking and diffuse ownership. */
vec3 VgeFiniteDiskSpecularBRDF(vec3 N, vec3 V, vec3 L, vec3 F0, float roughness, float angularRadius)
{
    float r = clamp(roughness, 0.04, 1.0);
    float sineRadius = sin(clamp(angularRadius, 0.0, 1.57079632679)) * (1.0 - r * r);
    if (sineRadius <= 0.0) return cookTorranceBRDF(N, V, L, F0, r);
    float nv = clamp(dot(N, V), 0.0, 1.0);
    float nl = clamp(dot(N, L), 0.0, 1.0);
    if (nv <= 0.0 || nl <= 0.0) return vec3(0.0);

    float nh, vh;
    if (dot(reflect(-V, N), L) >= sqrt(max(0.0, 1.0 - sineRadius * sineRadius)))
    {
        // The mirror half-vector is exactly N. Avoid cancellation in V+reflection
        // when the view is almost tangent to the surface.
        nh = 1.0;
        vh = nv;
    }
    else
    {
        vec3 selected = VgeFiniteDiskRepresentativeDirection(N, V, L, sineRadius);
        vec3 halfVector = V + selected;
        halfVector *= inversesqrt(max(dot(halfVector, halfVector), 1e-12));
        nh = clamp(dot(N, halfVector), 0.0, 1.0);
        vh = clamp(dot(V, halfVector), 0.0, 1.0);
    }
    float alpha = r * r;
    float a2 = alpha * alpha;
    // Compensate the broadened angular footprint without changing authored roughness.
    // Source extent and view geometry determine the reduction in peak energy.
    float extendedA2 = a2 + 0.25 * sineRadius * (3.0 * alpha + sineRadius) / (vh + 0.001);
    float distributionDenominator = (1.0 - nh * nh) + a2 * nh * nh;
    // Evaluate the exact mirror peak separately: compiler reassociation of the general
    // denominator can otherwise lose precision at the minimum supported roughness.
    float compensatedDistribution = nh == 1.0 ? 1.0 / (VGE_PI * extendedA2)
        : (a2 / distributionDenominator) * (a2 / distributionDenominator) / (VGE_PI * extendedA2);
    float geometry = geometrySchlickGGX(nv, r) * geometrySchlickGGX(nl, r);
    vec3 fresnel = fresnelSchlick(vh, F0);
    return fresnel * compensatedDistribution * geometry / (4.0 * nv * nl + 0.000001);
}
#endif
