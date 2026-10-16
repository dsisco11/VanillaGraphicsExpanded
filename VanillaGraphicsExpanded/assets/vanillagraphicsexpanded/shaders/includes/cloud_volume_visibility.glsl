/** Recomputes only full-view density/depth to reject silhouette and thin-column reconstruction errors. */
void CloudVisibility(vec2 uv,inout CloudPayload result)
{
    vec2 ndc=uv*2.0-1.0;
    vec3 direction=normalize(CloudUnproject(ndc,1.0));
    // Native clips at both opaque and liquid depth. Normalized UV handles quarter-size liquid storage.
    float limit=min(length(CloudUnproject(ndc,result.depth.z)),length(CloudUnproject(ndc,result.depth.w)));
    vec3 origin=-volumeOffsetWidth.xyz;
    origin.xz+=volumeOffsetWidth.w*25.0;
    CloudRay ray;
    if(!CloudStart(origin,direction,limit,ray)) return;
    vec4 k=vec4(0); vec3 reveal=vec3(1); float transmission=1.0;
    for(int cellIndex=0;cellIndex<402;cellIndex++)
    {
        vec4 data=CloudColumn(ray.cell); float near,far;
        if(CloudInterval(ray,data,near,far))
        {
            int count=volumeDetail.w>.5?int(volumeSamples.x):1;
            float ds=(far-near)/float(count);
            for(int i=0;i<16;i++)
            {
                if(i>=count) break;
                float t=near+(float(i)+.5)*ds;
                vec3 local=ray.o+direction*t;
                float sigma=CloudDensity(local,ray.cell,data);
                float alpha=clamp(1.0-exp(-sigma*(ds*volumeAnchorUnits.z)),0.0,1.0);
                if(alpha<=0.0) continue;
                if(result.depth.y==0.0) result.depth.x=near;
                result.depth.y=far;
                // Visibility validation repeats view density only; secondary light rays remain reduced.
                transmission*=1.0-alpha;
                float bin=log(CloudAdjustedDepth(near+float(i)*ds)/30.0+1.0);
                vec3 b=vec3(vgeOitBellCurve(bin),vgeOitBellCurve(bin-1.0),bin>2.0?1.0:vgeOitBellCurve(bin-2.0));
                reveal*=clamp(vec3(1)-alpha*b,0.0,1.0);
            }
        }
        if(transmission<1e-6||!CloudAdvance(ray)) break;
    }
    k.a=clamp(1.0-transmission,0.0,1.0);
    result.bins=clamp(reveal,0.0,1.0); result.reveal=clamp(transmission,0.0,1.0);
    result.a0=k*(1.0-reveal.x); result.a1=k*(1.0-reveal.y); result.a2=k*(1.0-reveal.z);
    result.glow=vec4(0,0,0,k.a);
}
