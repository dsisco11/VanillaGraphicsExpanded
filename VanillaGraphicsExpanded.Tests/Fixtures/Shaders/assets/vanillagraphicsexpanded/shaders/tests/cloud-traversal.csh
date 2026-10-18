#version 430 core
#define VGE_CLOUD_PMJ
#define VGE_CLOUD_HIERARCHY
@import "../includes/cloud_volume_parameters.glsl"
@import "../includes/cloud_volume_sampling.glsl"
@import "../includes/cloud_volume_density.glsl"
@import "../includes/cloud_volume_hierarchy.glsl"
@import "../includes/cloud_volume_light.glsl"
@import "../includes/cloud_volume_primary.glsl"
layout(local_size_x=1) in;
layout(std140) uniform CloudRayTestInputs { vec4 testOriginLimit; vec4 testDirectionMode; };
layout(std430) buffer CloudTraversalTestResult { vec4 intervals[402]; vec4 counts; };

/** Exposes actual production traversal intervals while counting native leaf probes independently of helper internals. */
void main()
{
    counts=vec4(0);
    if(testDirectionMode.w==6.0)
    {
        // The independent CPU oracle supplies arbitrary query positions, not this shader's lattice.
        for(int i=0;i<402;i++)
        {
            if(i>=int(testOriginLimit.w)) break;
            vec3 p=testOriginLimit.xyz+testDirectionMode.xyz*intervals[i].x;
            ivec2 cell=ivec2(floor(p.xz/50.0));
            intervals[i].z=CloudDensity(p,cell,CloudColumn(cell));
        }
        return;
    }
    if(testDirectionMode.w>=4.0)
    {
        vec2 span;
        if(!CloudPrimarySpan(testOriginLimit.xyz,testDirectionMode.xyz,testOriginLimit.w,span)) return;
        int count=CloudPrimaryCount(span.y-span.x);
        float ds=(span.y-span.x)/float(count);
        float jitter=testDirectionMode.w==4.0?.5:.173;
        for(int i=0;i<384;i++)
        {
            if(i>=count) break;
            float t=CloudPrimaryPosition(span,count,i,jitter);
            vec3 p=testOriginLimit.xyz+testDirectionMode.xyz*t;
            ivec2 cell=ivec2(floor(p.xz/50.0));
            float sigma=CloudDensity(p,cell,CloudColumn(cell));
            intervals[i]=vec4(t,ds,sigma,0);
            counts.z+=sigma*ds;
        }
        counts.x=float(count); counts.y=span.x; counts.w=span.y;
        return;
    }
    if(testDirectionMode.w>=2.0)
        counts.z=CloudLightDepth(testOriginLimit.xyz,testDirectionMode.xyz,.37);
    CloudRay ray;
    if(!CloudStart(testOriginLimit.xyz,testDirectionMode.xyz,testOriginLimit.w,ray)) return;
    for(int step=0;step<402;step++)
    {
        if(testDirectionMode.w>0.0 && !CloudHierarchySeek(ray)) break;
        counts.y+=1.0;
        vec4 data=CloudColumn(ray.cell);
        float near,far;
        if(CloudInterval(ray,data,near,far))
        {
            intervals[int(counts.x)]=vec4(near,far,vec2(ray.cell));
            counts.x+=1.0;
        }
        if(!CloudAdvance(ray)) break;
    }
}
