#version 430 core
@import "./includes/lumon_octahedral.glsl"
@import "./includes/pbr_persistent_lighting.glsl"
layout(local_size_x=64) in;
struct Answer { ivec4 status; ivec4 cell; ivec4 normal; vec4 fraction; vec4 radiance; };
layout(std430,binding=0) readonly buffer ResidentAnswers { Answer answers[]; };
struct Sample { ivec4 target; vec4 value; };
layout(std430,binding=1) readonly buffer Commit {
    ivec4 probe; vec4 ao; vec4 scalars; Sample samples[];
};
layout(rgba16f,binding=0) writeonly uniform image2D radianceAtlas;
layout(rgba16f,binding=1) writeonly uniform image2D visibilityAtlas;
layout(rg16f,binding=2) writeonly uniform image2D distanceAtlas;
layout(rg32f,binding=3) writeonly uniform image2D metadataAtlas;
shared uint rejected;
/** Writes every admitted ready direction before exposing this admission's valid metadata. */
void main()
{
    if (gl_LocalInvocationIndex == 0u)
    {
        bool validScalars = !any(isnan(scalars.xyz)) && !any(isinf(scalars.xyz))
            && all(greaterThanEqual(scalars.xyz, vec3(0)))
            && scalars.x <= 1.0 && scalars.y <= 1.0 && scalars.z <= VGE_STORAGE_HALF_MAX;
        bool validVisibility = !any(isnan(ao)) && !any(isinf(ao))
            && dot(ao.xyz, ao.xyz) > 0.0 && !isinf(dot(ao.xyz, ao.xyz)) && ao.w >= 0.0 && ao.w <= 1.0;
        rejected = validScalars && validVisibility ? 0u : 1u;
    }
    barrier();
    // Validate the entire directional publication before touching RGB or scalar metadata.
    // Failed answers retain the old coherent probe, never freshly valid stale directions.
    for(uint i=gl_LocalInvocationIndex;i<uint(probe.w);i+=64u)
    {
        Sample entry = samples[i];
        vec3 physical = entry.target.z >= 0 ? answers[entry.target.z].radiance.xyz : entry.value.rgb;
        if (!VgeDirectionalLightAdmitted(physical) || isnan(entry.value.a) || isinf(entry.value.a)
            || abs(entry.value.a) > VGE_STORAGE_HALF_MAX) atomicOr(rejected, 1u);
    }
    barrier();
    if (rejected != 0u) return;
    for(uint i=gl_LocalInvocationIndex;i<uint(probe.w);i+=64u)
    {
        Sample entry=samples[i];
        vec4 value=entry.value;
        if(entry.target.z>=0) value.xyz=answers[entry.target.z].radiance.xyz;
        vec3 stored;
        // An unsupported answer retains its old direction; it cannot become a valid black hit.
        if (VgeDirectionalLightAdmitted(value.rgb) && VgeTryStorePersistentLight(value.rgb,false,stored))
            imageStore(radianceAtlas,probe.xy*probe.z+entry.target.xy,vec4(stored,value.a));
    }
    memoryBarrierImage(); barrier();
    if(gl_LocalInvocationIndex==0u)
    {
        imageStore(visibilityAtlas,probe.xy,vec4(lumonDirectionToOctahedralUV(normalize(ao.xyz)),scalars.x,ao.w));
        imageStore(distanceAtlas,probe.xy,vec4(scalars.z,0,0,0));
        imageStore(metadataAtlas,probe.xy,vec4(scalars.y,scalars.w,0,0));
    }
}
