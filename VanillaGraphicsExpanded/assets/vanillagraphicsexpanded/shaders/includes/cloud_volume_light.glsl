#ifndef VGE_CLOUD_VOLUME_LIGHT
#define VGE_CLOUD_VOLUME_LIGHT
/** Integrates a bounded secondary ray through the same density and supported map domain. */
float CloudLightDepth(vec3 local,vec3 direction,float jitter)
{
    CloudRay ray;
    if(!CloudStart(local,direction,1e7,ray)) return 0.0;
    float tau=0.0;
    for(int cellIndex=0;cellIndex<402;cellIndex++)
    {
#ifdef VGE_CLOUD_HIERARCHY
        if(!CloudHierarchySeek(ray)) break;
#endif
        vec4 data=CloudColumn(ray.cell); float near,far;
        if(CloudInterval(ray,data,near,far))
        {
            int count=volumeDetail.w>.5?int(volumeSamples.y):1;
            float ds=(far-near)/float(count);
            float phase=volumeDetail.w>.5?CloudSegmentJitter(jitter,ray.cell,0,0x4C494748u):.5;
            for(int i=0;i<4;i++)
            {
                if(i>=count) break;
                tau=min(1e30,tau+CloudDensity(ray.o+ray.d*(near+(float(i)+phase)*ds),ray.cell,data)*(ds*volumeAnchorUnits.z));
            }
        }
        // Saturation must include the approximate octave, including its unoccluded endpoint.
        // Keep tau finite so a zero occlusion coefficient never forms zero times infinity.
        if(tau>=64.0&&(volumeSamples.w==0.0||tau*volumeSamples.w>=32.0)) return tau;
        if(!CloudAdvance(ray)) break;
    }
    return tau;
}
#endif
