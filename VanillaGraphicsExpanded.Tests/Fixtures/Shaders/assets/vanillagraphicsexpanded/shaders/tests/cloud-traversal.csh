#version 430 core
@import "../includes/cloud_volume_parameters.glsl"
@import "../includes/cloud_volume_sampling.glsl"
@import "../includes/cloud_volume_density.glsl"
@import "../includes/cloud_volume_hierarchy.glsl"
layout(local_size_x=1) in;
layout(std140) uniform CloudRayTestInputs { vec4 testOriginLimit; vec4 testDirectionMode; };
layout(std430) buffer CloudTraversalTestResult { vec4 intervals[402]; vec4 counts; };

/** Exposes actual production traversal intervals while counting native leaf probes independently of helper internals. */
void main()
{
    counts=vec4(0);
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
