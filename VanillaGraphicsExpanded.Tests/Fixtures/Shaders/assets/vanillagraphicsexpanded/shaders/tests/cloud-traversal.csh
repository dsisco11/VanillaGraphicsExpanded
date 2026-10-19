#version 430 core
#define VGE_CLOUD_PRIMARY_NOISE
#define VGE_CLOUD_HIERARCHY
@import "../includes/cloud_volume_parameters.glsl"
@import "../includes/cloud_volume_sampling.glsl"
@import "../includes/cloud_volume_density.glsl"
@import "../includes/cloud_volume_hierarchy.glsl"
@import "../includes/cloud_shadow_record.glsl"
@import "../includes/cloud_shadow_frame.glsl"
layout(std430) buffer CloudTraversalTestResult { vec4 intervals[402]; vec4 counts; };
vec3 measuredOrigin; int measuredSamples=0;
/** Records only actual full-density calls made by the production secondary loop. */
float MeasuredShadowDensity(vec3 point,ivec2 cell,vec4 data)
{
    float density=CloudDensity(point,cell,data);
    if(measuredSamples<402) intervals[measuredSamples]=vec4(length(point-measuredOrigin),density,point.y,0);
    measuredSamples++; return density;
}
/** Preserves production descriptor reuse while recording the same full-density calls as the uncached fixture. */
float MeasuredShadowDensity(vec3 point,ivec2 cell,vec4 data,inout CloudDensityCache cache)
{
    float density=CloudDensity(point,cell,data,cache);
    if(measuredSamples<402) intervals[measuredSamples]=vec4(length(point-measuredOrigin),density,point.y,0);
    measuredSamples++; return density;
}
#define CloudDensity MeasuredShadowDensity
@import "../includes/cloud_volume_light.glsl"
#undef CloudDensity
@import "../includes/cloud_volume_primary.glsl"
#define CloudDensity MeasuredShadowDensity
@import "../includes/cloud_shadow_support.glsl"
#undef CloudDensity
layout(local_size_x=1) in;
layout(std140) uniform CloudRayTestInputs { vec4 testOriginLimit; vec4 testDirectionMode; };
uniform sampler2D shadowRecordMap;

/** Retains the original per-corner wrapping independently of production lattice reuse. */
float ReferenceCloudHash(vec3 lattice,float period)
{
    vec3 p=mod(mod(lattice,period),289.0);
    return fract(sin(dot(p,vec3(17.13,91.71,43.19)))*43758.5453);
}
/** Retains the original interpolated noise field for arithmetic-equivalence checks. */
float ReferenceCloudNoise(vec3 material)
{
    float period=max(1.0,floor(volumeDetail.z*12800.0+.5));
    vec3 p=material*(period/12800.0),i=floor(p),f=fract(p);
    f=f*f*(3.0-2.0*f);
    return mix(mix(mix(ReferenceCloudHash(i,period),ReferenceCloudHash(i+vec3(1,0,0),period),f.x),
        mix(ReferenceCloudHash(i+vec3(0,1,0),period),ReferenceCloudHash(i+vec3(1,1,0),period),f.x),f.y),
        mix(mix(ReferenceCloudHash(i+vec3(0,0,1),period),ReferenceCloudHash(i+vec3(1,0,1),period),f.x),
        mix(ReferenceCloudHash(i+vec3(0,1,1),period),ReferenceCloudHash(i+vec3(1,1,1),period),f.x),f.y),f.z);
}
/** Retains the original density arithmetic as an algorithm-equivalence oracle, independent of cache, wrapping reuse and face pruning. */
float ReferenceCloudDensity(vec3 local,ivec2 cell,vec4 data)
{
    if(data.r<=0.0||local.y<data.b*50.0||local.y>data.a*50.0) return 0.0;
    float detail=1.0;
    if(volumeDetail.w>.5)
    {
        vec2 f=local.xz-vec2(cell)*50.0;
        float edge=min(local.y-data.b*50.0,data.a*50.0-local.y);
        if(!CloudOccupied(cell+ivec2(-1,0),local.y)) edge=min(edge,f.x);
        if(!CloudOccupied(cell+ivec2(1,0),local.y)) edge=min(edge,50.0-f.x);
        if(!CloudOccupied(cell+ivec2(0,-1),local.y)) edge=min(edge,f.y);
        if(!CloudOccupied(cell+ivec2(0,1),local.y)) edge=min(edge,50.0-f.y);
        vec3 material=local+vec3(volumeAnchorUnits.x,0,volumeAnchorUnits.y);
        detail=(1.0+volumeDetail.x*(2.0*ReferenceCloudNoise(material)-1.0))*smoothstep(0.0,volumeDetail.y,edge);
    }
    return (data.r/50.0)/volumeAnchorUnits.z*detail;
}

/** Exposes bounded production schedules and shared density through independent authored queries. */
void main()
{
    counts=vec4(0); measuredOrigin=testOriginLimit.xyz;
    if(testDirectionMode.w==19.0)
    {
        int source=int(testOriginLimit.w);
        vec3 light=source==0?volumeSun.xyz:volumeMoon.xyz,center,u,v,radius;
        bool valid=CloudShadowFrame(source,light,center,u,v,radius);
        intervals[0]=vec4(center,0);intervals[1]=vec4(u,0);
        intervals[2]=vec4(v,0);intervals[3]=vec4(radius,0);
        counts=vec4(valid?1:0,0,0,0);return;
    }
    if(testDirectionMode.w==18.0)
    {
        vec3 origin=testOriginLimit.xyz,direction=normalize(testDirectionMode.xyz);
        vec2 clipped,chord=vec2(0);float length=0.0,tau=0.0;bool valid=true;
        if(CloudShadowDomainSpan(origin,direction,testOriginLimit.w,clipped))
        {
            valid=CloudShadowSupportMeasure(origin,direction,clipped,chord,length);
            if(valid)valid=CloudShadowSupportIntegrate(origin,direction,clipped,length,
                int(volumeSamples.y),1.0,tau);
        }
        intervals[401]=vec4(chord,CloudShadowSupportCount(length,int(volumeSamples.y)),0);
        counts=vec4(length,measuredSamples,tau,valid?1.0:0.0);return;
    }
    if(testDirectionMode.w==15.0)
    {
        vec2 transmission;
        bool valid=CloudShadowTransmission(shadowRecordMap,testOriginLimit.xy,
            testOriginLimit.w,testDirectionMode.x,testDirectionMode.y,transmission);
        counts=vec4(transmission,valid?1.0:0.0,0);return;
    }
    if(testDirectionMode.w==14.0)
    {
        float tau;
        bool valid=CloudShadowRecordDepth(texelFetch(cloudMap,ivec2(testOriginLimit.xy),0).rgb,
            testOriginLimit.w,testDirectionMode.x,tau);
        counts=vec4(tau,valid?1.0:0.0,exp(-tau),0);return;
    }
    if(testDirectionMode.w==13.0)
    {
        vec3 response=CloudDirectionalUnshadowed(volumeSolar.xyz,testOriginLimit.w)
            ?volumeSolar.xyz*(testOriginLimit.w+volumeSamples.z/12.5663706144)
            :CloudDirectional(testOriginLimit.xyz,normalize(testDirectionMode.xyz),volumeSolar.xyz,testOriginLimit.w);
        intervals[401]=vec4(response,0);counts=vec4(measuredSamples,0,0,0);return;
    }
    if(testDirectionMode.w==12.0)
    {
        vec2 span=testOriginLimit.xy,supported=testDirectionMode.xy;
        int count=int(testOriginLimit.z);float jitter=testDirectionMode.z;
        ivec2 range=CloudPrimaryRange(span,supported,count,jitter);
        float ds=(span.y-span.x)/float(count),directTau=0.0,windowTau=0.0;
        for(int i=0;i<384;i++)
        {
            if(i>=count) break;
            float t=CloudPrimaryPosition(span,count,i,jitter);
            // A varying positive scalar field isolates scheduled-point preservation from
            // density textures, hierarchy construction and approximation-quality claims.
            float sigma=t>=supported.x&&t<=supported.y?.001+.0001*cos(t*.17):0.0;
            float selected=i>=range.x&&i<range.y?sigma:0.0;
            intervals[i]=vec4(t,sigma,selected,float(i));
            directTau+=sigma*ds;windowTau+=selected*ds;
        }
        counts=vec4(vec2(range),directTau,windowTau);return;
    }
    if(testDirectionMode.w==11.0)
    {
        CloudDensityCache cache=CloudDensityCacheStart();
        for(int i=0;i<402;i++)
        {
            if(i>=int(testOriginLimit.w)) break;
            vec3 point=testOriginLimit.xyz+testDirectionMode.xyz*intervals[i].x;
            ivec2 cell=ivec2(floor(point.xz/50.0));
            vec4 direct=CloudColumn(cell),cached=CloudDensityColumn(cell,cache);
            intervals[i].y=ReferenceCloudDensity(point,cell,direct);
            intervals[i].z=CloudDensity(point,cell,direct);
            intervals[i].w=CloudDensity(point,cell,cached,cache);
            counts.x+=1.0;
        }
        return;
    }
    if(testDirectionMode.w==10.0)
    {
        vec3 source=CloudDirectional(testOriginLimit.xyz,volumeSun.xyz,volumeSolar.xyz,CloudDirectionalPhase(vec3(0,0,-1),volumeSun.xyz))
            +CloudDirectional(testOriginLimit.xyz,volumeMoon.xyz,volumeLunar.xyz,CloudDirectionalPhase(vec3(0,0,-1),volumeMoon.xyz));
        intervals[400]=vec4(source,0);counts.x=float(measuredSamples);return;
    }
    if(testDirectionMode.w>=7.0)
    {
        counts.z=CloudLightDepth(testOriginLimit.xyz,testDirectionMode.xyz);
        counts.x=float(measuredSamples);
        return;
    }
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
}
