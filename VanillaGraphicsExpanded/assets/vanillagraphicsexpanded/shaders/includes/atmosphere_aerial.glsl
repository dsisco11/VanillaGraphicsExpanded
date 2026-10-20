#ifndef VGE_ATMOSPHERE_AERIAL
#define VGE_ATMOSPHERE_AERIAL
@import "./atmosphere_sky_mapping.glsl"
@import "./atmosphere_aerial_mapping.glsl"
uniform sampler3D vge_atmosphereAerialRadiance;
uniform sampler3D vge_atmosphereAerialAttenuation;
uniform sampler2D vge_lightShaftOcclusion;
#ifndef VGE_ATMOSPHERE_SCATTERING_DIRECTIONS
// Legacy two-band fixtures need only the function's supplied source direction.
#define vge_atmosphereScatteringSun sun
#define vge_atmosphereScatteringMoon sun
#endif

// Angular coordinates share the sky. Distance slices share fixed metre ranges across
// every direction, preventing ground/space neighbours from mixing different ray lengths.
vec3 VgeApplyAerial(vec3 radiance, vec3 worldDisplacement, float skyVisibility, vec3 parameters, vec3 sun)
{
    float distanceMetres = length(worldDisplacement);
    if (distanceMetres <= .0001 || skyVisibility <= 0.0) return radiance;
    vec3 direction = worldDisplacement / distanceMetres;
    float azimuth = dot(direction.xz, direction.xz) > .0000001 ? atan(direction.z, direction.x) : 0.0;
    float row = atmSkyCoordinate(asin(clamp(direction.y, -1.0, 1.0)), parameters.y);
    float slice = clamp(log(1.0 + distanceMetres) / log(1.0 + 2500000.0), 0.0, 1.0);
    vec3 size = vec3(textureSize(vge_atmosphereAerialAttenuation, 0));
    vec3 uvw = vec3(azimuth / 6.28318530718, (row * (size.y - 1.0) + .5) / size.y,
        (slice * (size.z - 1.0) + .5) / size.z);
    float bands = float(textureSize(vge_atmosphereAerialRadiance, 0).y) / size.y;
    vec3 packedUv = vec3(uvw.x, uvw.y / bands, uvw.z);
    vec3 scatter = texture(vge_atmosphereAerialRadiance, packedUv).rgb
        + texture(vge_atmosphereAerialRadiance, packedUv + vec3(0.0, 1.0 / bands, 0.0)).rgb
            * atmMieFactor(direction, bands > 2.5 ? vge_atmosphereScatteringSun : sun);
    if (bands > 2.5)
        scatter += texture(vge_atmosphereAerialRadiance, packedUv + vec3(0.0, 2.0 / bands, 0.0)).rgb
            * atmMieFactor(direction, vge_atmosphereScatteringMoon);
    scatter *= parameters.z > 0.0 ? parameters.z : 1.0;
    vec3 loss = clamp(texture(vge_atmosphereAerialAttenuation, uvw).rgb, 0.0, 1.0);
    // Receiver sky availability gates both terms: closed interiors cannot absorb
    // light into an outdoor medium while receiving none of its in-scattering.
    float availability = clamp(skyVisibility, 0.0, 1.0);
    // Screen-space shafts modulate only incoming atmospheric light, never extinction or surface radiance.
    vec2 shaftSize=vec2(max(textureSize(vge_lightShaftOcclusion,0),ivec2(1)));
    #ifdef VGE_ATMOSPHERE_FRAGMENT_COORD
    vec2 shaftPixel=VGE_ATMOSPHERE_FRAGMENT_COORD;
    #else
    vec2 shaftPixel=gl_FragCoord.xy;
    #endif
    float shaftVisibility=1.0-clamp(texture(vge_lightShaftOcclusion,shaftPixel/shaftSize).r,0.0,1.0);
    return radiance * (vec3(1.0) - loss * availability) + scatter * availability * shaftVisibility;
}
#endif
