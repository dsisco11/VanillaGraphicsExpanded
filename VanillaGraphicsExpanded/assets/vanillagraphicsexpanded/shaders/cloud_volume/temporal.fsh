#version 430 core
@import "../includes/vge_frame_ubo.glsl"
@import "../includes/pbr_frame_lighting.glsl"
@import "../includes/cloud_temporal_parameters.glsl"
@import "../includes/camera_reprojection.glsl"
@import "../includes/blue_noise.glsl"
@import "../includes/interleaved_sampling.glsl"
@import "sampling.glsl"
uniform sampler2D temporalBlueNoise;
uniform sampler2D cloudPmjJitter;
uniform sampler2D reconstructCloud;
uniform sampler2D reconstructBins;
uniform sampler2D reconstructMetadata;
uniform sampler2D reconstructMaterialDepth;
uniform sampler2D reconstructVisibilityDepth;
uniform sampler2D reconstructLiquidDepth;
uniform sampler2D previousAge;
uniform sampler2D previousCloud;
uniform sampler2D previousBins;
uniform sampler2D previousMetadata;
layout(location=0) out vec4 outCloud;
layout(location=1) out vec3 outBins;
layout(location=2) out vec4 outMetadata;
layout(location=3) out float outAge;
#if VGE_CLOUD_TEMPORAL_DIAGNOSTICS
layout(location=4) out vec4 outDiagnostics; // prior weight, update status, rejection, observation age
#endif
// Required age and optional diagnostic resources have independent shader interfaces.
#if VGE_CLOUD_TEMPORAL_DIAGNOSTICS
#define StoredTemporalReason(value) reason=value
#define StoredTemporalReject(value) outDiagnostics.z=value
#define StoredTemporalReceipt(weight,status,rejection,age) outDiagnostics=vec4(weight,status,rejection,age)
#else
#define StoredTemporalReason(value)
#define StoredTemporalReject(value)
#define StoredTemporalReceipt(weight,status,rejection,age)
#endif
/** Admits only completed finite physical payloads, including certified empty rays. */
bool StoredTemporalFinite(vec4 cloud,vec3 bins,vec4 meta)
{
    return (meta.y==1||meta.y==2)&&!any(isnan(meta))&&!any(isinf(meta))
        &&!any(isnan(cloud))&&!any(isinf(cloud))&&all(greaterThanEqual(cloud,vec4(0)))&&cloud.a<=1
        &&!any(isnan(bins))&&!any(isinf(bins))&&all(greaterThanEqual(bins,vec3(0)))&&all(lessThanEqual(bins,vec3(1)))
        &&(meta.y!=2||(all(equal(cloud,vec4(0)))&&all(equal(bins,vec3(1)))));
}
/** Uses the same conservative full-pixel footprint as the configured reduced trace. */
float StoredTemporalDepth(vec2 uv)
{
    vec4 depth=vec4(1);int divisor=int(cloudLayout.w);
    for(int y=0;y<divisor/2;y++)for(int x=0;x<divisor/2;x++)
    {
        vec2 tap=uv+(vec2(x,y)*2-float(divisor/2-1))/vgeFrame.screenSize;
        depth=min(depth,min(textureGather(reconstructMaterialDepth,tap,0),
            min(textureGather(reconstructVisibilityDepth,tap,0),textureGather(reconstructLiquidDepth,tap,0))));
    }
    return min(min(depth.x,depth.y),min(depth.z,depth.w));
}
/** Identifies actual observations independently of spatial fallback or carried history. */
bool StoredTemporalFresh(ivec2 p,ivec2 size,out ivec2 source,out float rank)
{
    source=p;rank=0;if(cloudHistory.x<.5)return true;
    source=p/2;rank=BlueNoiseRank(temporalBlueNoise,source,0u,8);
    return all(equal(p,InterleavedSampleLocation2x2(rank,source,vgeFrame.frameIndex,size)));
}
/** Classifies one history tap without relaxing foreground, support, distance or expiry gates. */
float StoredTemporalTap(vec4 old,float age,float kind,float depth,float distance)
{
    if(old.y!=kind||old.w<1||any(isnan(old))||any(isinf(old)))return 4;
    if(abs(old.x-depth)>2e-5)return 5;
    if(kind==1&&abs(old.z-distance)>max(2.0,distance*.05))return 7;
    if(age<0||age>=7||isnan(age)||isinf(age))return 8;
    return 0;
}
/** Uses a qualified hardware footprint, or its nearest individually compatible texel centre. */
bool StoredTemporalHistory(vec2 uv,vec2 sampleUv,float depth,float distance,float kind,bool fresh,ivec2 size,
    out vec4 value,out vec3 bins,out float count,out float age,out float reason,out vec2 support)
{
    value=vec4(0);bins=vec3(1);count=8;age=0;reason=0;StoredTemporalReason(1);support=vec2(0);
    if(cloudHistory.x<.5)return false;
    vec3 direction=normalize(CameraReconstructDisplacement(vgeFrame.invProjectionMatrix,vgeFrame.invViewMatrix,sampleUv,1));
    // Rotation maps directions independently of cloud distance. Translation needs current-ray
    // evidence: main supplies the nearest completed fresh neighbor as a bounded distance proxy.
    // Never bootstrap a moving ray from history at the old, unrelated same-screen coordinate.
    bool translated=any(notEqual(cloudPreviousTranslation.xyz,vec3(0)));
    bool directional=!fresh&&!translated;
    float anchor=distance;
    if(directional)anchor=1;
    if(!directional&&kind==2)anchor=length(CameraReconstructDisplacement(vgeFrame.invProjectionMatrix,vgeFrame.invViewMatrix,sampleUv,depth));
    if(!directional&&kind!=1&&kind!=2){StoredTemporalReason(4);return false;}
    if(anchor<=0){StoredTemporalReason(4);return false;}
    vec3 point=direction*anchor+cloudPreviousTranslation.xyz;
    vec4 clip=CameraProjectDisplacement(vgeFrame.prevViewProjMatrix,vgeFrame.projectionMatrix,point);
    if(clip.w<=0){StoredTemporalReason(2);return false;}
    // Carried history represents the reduced pixel footprint. Its centre can use exact
    // stationary addressing; a newly jittered observation must project its actual ray instead.
    bool stationary=cloudHistory.z>.5&&all(equal(sampleUv,uv));
    vec2 oldUv=stationary?uv:CameraClipUv(clip);
    if(!CameraUvInBounds(oldUv)){StoredTemporalReason(2);return false;}
    // The certified identity map has exactly one support texel. Even an inverse/project
    // roundoff of a millionth of a pixel must not import neighboring confidence or age.
    vec2 coordinate=stationary?vec2(ivec2(gl_FragCoord.xy)):oldUv*vec2(size)-.5;
    vec2 fraction=fract(coordinate);ivec2 base=ivec2(floor(coordinate));
    // Project the CURRENT occluder into the PREVIOUS camera, without material wind. Comparing
    // raw projection depths from different cameras would reject unchanged scene geometry.
    vec4 sceneView=CameraReconstructPosition(vgeFrame.invProjectionMatrix,uv,depth);
    // Shared history is already rebased to the current render origin. Preserve the native
    // eye translation (w=1); no cloud placement, weather height or wind enters this path.
    vec3 scenePosition=(vgeFrame.invViewMatrix*vec4(sceneView.xyz/sceneView.w,1)).xyz;
    vec4 sceneClip=CameraProjectPosition(vgeFrame.prevViewProjMatrix,scenePosition);
    if(sceneClip.w<=0){StoredTemporalReason(5);return false;}
    float expectedDepth=sceneClip.z/sceneClip.w*.5+.5;
    if(directional)
    {
        // The address is established by rotation, not by the candidate's own metadata. Choose
        // nearest valid support there to establish classification/distance before admitting a
        // coherent bilinear footprint. This also preserves exact own-pixel stationary history.
        float nearestSupport=3.402823e38;bool established=false;
        for(int y=0;y<2;y++)for(int x=0;x<2;x++)
        {
            float weight=(x==0?1-fraction.x:fraction.x)*(y==0?1-fraction.y:fraction.y);
            if(weight==0)continue;
            ivec2 p=clamp(base+ivec2(x,y),ivec2(0),size-1);
            vec4 candidate=texelFetch(previousMetadata,p,0);
            // A stationary footprint has one tap: its age is checked once in the common pass.
            float candidateAge=stationary?0:texelFetch(previousAge,p,0).r;
            if((candidate.y!=1&&candidate.y!=2)||(candidate.y==1&&candidate.z<=0))
            {StoredTemporalReason(4);continue;}
            float rejection=StoredTemporalTap(candidate,candidateAge,candidate.y,expectedDepth,candidate.z);
            if(rejection!=0){StoredTemporalReason(rejection);continue;}
            vec2 offset=vec2(p)-coordinate;float separation=dot(offset,offset);
            if(separation<nearestSupport)
            {nearestSupport=separation;kind=candidate.y;anchor=candidate.z;established=true;}
        }
        if(!established)return false;
    }
    bool filtered=true,found=false;float nearest=3.402823e38,selectedCount=0,selectedAge=0;
    float expectedDistance=directional?anchor:length(point),filteredDistance=0,selectedDistance=0,inverseConfidence=0;
    vec2 selectedUv=oldUv;
    for(int y=0;y<2;y++)for(int x=0;x<2;x++)
    {
        float weight=(x==0?1-fraction.x:fraction.x)*(y==0?1-fraction.y:fraction.y);
        if(weight==0)continue;
        ivec2 p=clamp(base+ivec2(x,y),ivec2(0),size-1);
        vec4 old=texelFetch(previousMetadata,p,0);float observationAge=texelFetch(previousAge,p,0).r;
        float rejection=StoredTemporalTap(old,observationAge,kind,expectedDepth,expectedDistance);
        if(rejection!=0){filtered=false;StoredTemporalReason(rejection);continue;}
        // Transport and its freshness use the same admitted bilinear weights. Confidence is
        // a weighted harmonic mean: interpolation cannot manufacture independent observations
        // or exceed the strongest input, and a tiny weak tap no longer controls the footprint.
        inverseConfidence+=weight/min(old.w,8);
        age+=weight*observationAge;
        filteredDistance+=weight*old.z;
        vec2 centre=(vec2(p)+.5)/vec2(size),offset=vec2(p)-coordinate;
        float separation=dot(offset,offset);
        if(separation<nearest)
        {
            found=true;nearest=separation;selectedUv=centre;
            selectedCount=old.w;selectedAge=observationAge;selectedDistance=old.z;
        }
    }
    if(!found)return false;
    // A mixed footprint cannot interpolate across incompatible cloud/foreground support. Read
    // the nearest admitted centre through the same hardware samplers, preserving the paired
    // radiance, opacity and bucket products rather than blending them with rejected taps.
    if(filtered)count=clamp(1/inverseConfidence,1,8);
    else{oldUv=selectedUv;count=selectedCount;age=selectedAge;}
    if(!fresh&&kind==1)
    {
        // History distances belong to the previous camera. Recover a distance on the current
        // ray from |direction*distance + translation| = previousDistance. The independent
        // current neighbor already bounded translated correspondence; this is metadata transport,
        // not another observation or an exact surface reconstruction.
        float previousDistance=filtered?filteredDistance:selectedDistance;
        anchor=previousDistance;
        if(translated)
        {
            float along=dot(direction,cloudPreviousTranslation.xyz);
            float square=previousDistance*previousDistance-dot(cloudPreviousTranslation.xyz,cloudPreviousTranslation.xyz)+along*along;
            if(square<=0){StoredTemporalReason(7);return false;}
            anchor=sqrt(square)-along;
        }
        if(anchor<=0){StoredTemporalReason(7);return false;}
    }
    value=textureLod(previousCloud,oldUv,0);bins=textureLod(previousBins,oldUv,0).rgb;
    if(!StoredTemporalFinite(value,bins,vec4(depth,kind,anchor,count))){StoredTemporalReason(6);return false;}
    support=vec2(kind,kind==2?0:anchor);StoredTemporalReason(filtered?0:12);return true;
}
/** Preserves untraced observations; only actual fresh rays advance observation confidence. */
void main()
{
    outCloud=vec4(0);outBins=vec3(1);outMetadata=vec4(0);outAge=0;StoredTemporalReceipt(0,0,1,0);
    if(!VgeFrameLightingValid(vgeFrame.frameIndex,VgeLightingCalibrationGeneration()))return;
    ivec2 pixel=ivec2(gl_FragCoord.xy),size=ivec2(cloudTrace.xy),source;
    vec2 uv=(vec2(pixel)+.5)/vec2(size);float depth=StoredTemporalDepth(uv);
    if(isnan(depth)||isinf(depth)){StoredTemporalReject(5);return;}
    float rank;bool fresh=StoredTemporalFresh(pixel,size,source,rank);
    vec2 sampleUv=uv;
    if(fresh&&cloudHistory.x>.5)
    {
        sampleUv=StoredVolumeSampleUv(cloudPmjJitter,rank,pixel,size,vgeFrame.frameIndex,floatBitsToUint(cloudHistory.w));
    }
    vec4 data=vec4(0),meta=vec4(0);vec3 bins=vec3(1);
    if(fresh)
    {
        data=texelFetch(reconstructCloud,source,0);bins=texelFetch(reconstructBins,source,0).rgb;
        meta=texelFetch(reconstructMetadata,source,0);
        // A failed ray is never repaired using previous completed prefixes.
        if(!StoredTemporalFinite(data,bins,meta)){StoredTemporalReject(9);return;}
    }
    vec4 low=vec4(3.402823e38),high=vec4(0);vec3 lowBins=vec3(1),highBins=vec3(0);
    float best=3.402823e38;int bounds=0;ivec2 lo=ivec2(2),hi=ivec2(-2);
    // Sparse neighbors initialize holes and may bound a FRESH update only with spatially
    // spanning, same-support evidence. One observation never defines clipping bounds.
    for(int y=-1;y<=1;y++)for(int x=-1;x<=1;x++)
    {
        ivec2 p=pixel+ivec2(x,y),s;float neighborRank;
        if(any(lessThan(p,ivec2(0)))||any(greaterThanEqual(p,size))||!StoredTemporalFresh(p,size,s,neighborRank))continue;
        vec4 m=texelFetch(reconstructMetadata,s,0),c=texelFetch(reconstructCloud,s,0);vec3 b=texelFetch(reconstructBins,s,0).rgb;
        if(!StoredTemporalFinite(c,b,m)||abs(m.x-depth)>2e-5)continue;
        if(fresh&&m.y==meta.y&&abs(m.z-meta.z)<=max(2.0,meta.z*.05))
        {low=min(low,c);high=max(high,c);lowBins=min(lowBins,b);highBins=max(highBins,b);bounds++;lo=min(lo,ivec2(x,y));hi=max(hi,ivec2(x,y));}
        float score=float(x*x+y*y);
        if(!fresh&&score<best){best=score;data=c;bins=b;meta=m;}
    }
    vec4 old;vec3 oldBins;float count,age,reason;vec2 support;
    bool accepted=StoredTemporalHistory(uv,sampleUv,depth,meta.z,meta.y,fresh,size,old,oldBins,count,age,reason,support);
    if(!fresh&&accepted)
    {
        // Hardware-filtered transport is carried intact, with no spatial-current blend and no
        // independent-observation increment. Only translated correspondence requires a current
        // neighbor; direction-only reprojection can carry without any current spatial support.
        outCloud=old;outBins=oldBins;outMetadata=vec4(depth,support.x,support.y,count);
        // With no fresh input anywhere in the carried support, the youngest possible age
        // advances by one per frame. Averaging cannot keep an unrefreshed history alive:
        // each tap must still pass expiry before it contributes on the next frame.
        outAge=age+1;StoredTemporalReceipt(1,2,reason,age+1);return;
    }
    if(!fresh&&best==3.402823e38){StoredTemporalReject(reason);return;}
    float weight=0;
    if(fresh&&accepted)
    {
        bool clipped=bounds>=4&&all(greaterThanEqual(hi-lo,ivec2(2)))&&
            (any(lessThan(old,low))||any(greaterThan(old,high))||any(lessThan(oldBins,lowBins))||any(greaterThan(oldBins,highBins)));
        // A complete clipping replacement is an explicit rejection, never a growing counter.
        if(clipped)StoredTemporalReason(10);
        else
        {
            float peak=max(max(max(data.r,data.g),data.b),max(max(old.r,old.g),old.b));
            float delta=max(max(abs(data.r-old.r),abs(data.g-old.g)),abs(data.b-old.b));
            float reactive=clamp(1-abs(old.a-data.a)*4,0,1)*clamp(1-delta/max(peak,1e-6),0,1);
            weight=min(count,7)/(min(count,7)+1)*reactive;
            if(weight==0)StoredTemporalReason(11);
        }
    }
    outCloud=mix(data,old,weight);outBins=mix(bins,oldBins,weight);
    // 1/(1-weight) is the declared equivalent fresh-observation count, not a frame counter.
    outMetadata=vec4(depth,meta.y,meta.z,fresh?1/(1-weight):0);
    StoredTemporalReceipt(weight,fresh?1:3,reason,0);
}
