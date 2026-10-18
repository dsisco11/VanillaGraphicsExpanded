/** Reconstructs camera-axis foreground distance so planar surfaces agree across neighboring rays. */
float CloudReconstructionDepth(vec2 uv,float depth)
{
    vec4 p=vgeFrame.invProjectionMatrix*vec4(uv*2.0-1.0,depth*2.0-1.0,1);
    return abs(p.z/p.w);
}

/** Rejects foreground discontinuities, while allowing clouds wholly in front of both clips. */
float CloudReconstructionWeight(vec4 meta,vec2 sampleUv,vec2 currentDepth,float limit)
{
    if(any(isnan(meta))||any(isinf(meta))) return 0.0;
    vec4 direction=vgeFrame.invProjectionMatrix*vec4(sampleUv*2.0-1.0,1,1);
    float cloudFar=meta.y*abs(direction.z)/length(direction.xyz);
    // A partially occluded reduced ray cannot be split into front/back radiance during reconstruction.
    // Reject it conservatively rather than leaking integrated background cloud over new geometry.
    if(meta.y>0.0&&cloudFar>limit+max(.01,limit*.0001)) return 0.0;
    vec2 sampleDistance=vec2(CloudReconstructionDepth(sampleUv,meta.z),CloudReconstructionDepth(sampleUv,meta.w));
    if(meta.y>0.0&&cloudFar<min(limit,min(sampleDistance.x,sampleDistance.y))-2.0) return 1.0;
    vec2 difference=abs(sampleDistance-currentDepth)/max(vec2(2),currentDepth*.01);
    float error=max(difference.x,difference.y);
    return error<=1.0?1.0/(1.0+error):0.0;
}

