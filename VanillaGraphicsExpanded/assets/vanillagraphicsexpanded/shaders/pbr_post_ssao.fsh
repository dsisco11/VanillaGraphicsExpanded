#version 330 core
@import "./includes/ambient_occlusion.glsl"
@import "./includes/gbuffer_layers.glsl"
uniform sampler2D depthImage;
uniform sampler2DArray surfaceImage;
uniform sampler2D depthHalf;
uniform sampler2D depthQuarter;
uniform sampler2D depthEighth;
layout(location=0) out vec4 outOcclusion;
/** Rejects empty or out-of-radius hierarchy intervals; reconstructs hits from exact depth to avoid false planes. */
float VgeAoDepth(vec2 uv, float footprint, float receiverDepth) {
    ivec2 pixel=ivec2(uv*vec2(textureSize(depthImage,0)));
    vec2 range;
    if(footprint>=8.0) range=texelFetch(depthEighth,pixel/8,0).rg;
    else if(footprint>=4.0) range=texelFetch(depthQuarter,pixel/4,0).rg;
    else if(footprint>=2.0) range=texelFetch(depthHalf,pixel/2,0).rg;
    else return texelFetch(depthImage,pixel,0).r;
    if(range.x>=1.0) return 1.0;
    float nearest=-VgeAoPosition(uv,range.x).z;
    float farthest=-VgeAoPosition(uv,range.y).z;
    if(nearest>receiverDepth+aoSampling.x || farthest<receiverDepth-aoSampling.x) return 1.0;
    return texelFetch(depthImage,pixel,0).r;
}
/** Reduces isolated foreground depth spikes without treating continuous sloped walls as thin sheets. */
float VgeAoThickness(vec2 uv, vec2 direction, float sampleDepth) {
    vec2 size=vec2(textureSize(depthImage,0));
    float gap=aoSampling.x;
    // Both neighbours must lie behind the candidate before its thickness is uncertain.
    for(int side=-1;side<=1;side+=2) {
        vec2 neighbour=uv+float(side)*direction*1.5/size;
        if(any(lessThan(neighbour,vec2(0)))||any(greaterThanEqual(neighbour,vec2(1)))) return 1.0;
        neighbour=(floor(neighbour*size)+0.5)/size;
        float depth=texture(depthImage,neighbour).r;
        float separation=depth>=1.0?aoSampling.x:-VgeAoPosition(neighbour,depth).z-sampleDepth;
        gap=min(gap,separation);
    }
    return min(1.0,aoSampling.y/max(gap,0.0001));
}
/** Integrates paired horizons in view-space slices; off-screen and missing geometry remain unoccluded. */
void main() {
    ivec2 size=textureSize(depthImage,0), start=ivec2(gl_FragCoord.xy)*int(aoFrame.z);
    ivec2 receiver=min(start,size-1); float depth=1.0;
    // Select a real foreground receiver instead of averaging foreground and sky into a fictitious surface.
    for(int y=0;y<2;y++) for(int x=0;x<2;x++) {
        ivec2 p=start+ivec2(x,y);
        if(any(greaterThanEqual(p,size))) continue;
        float d=texelFetch(depthImage,p,0).r;
        if(d<depth) {depth=d;receiver=p;}
    }
    if(depth>=1.0) {outOcclusion=vec4(1,0,0,0);return;}
    vec2 uv=(vec2(receiver)+0.5)/vec2(size);
    vec3 position=VgeAoPosition(uv,depth);
    vec4 encoded=texelFetch(surfaceImage,ivec3(receiver,VGE_SURFACE_NORMAL),0);
    vec3 normal=mat3(aoView)*(encoded.xyz*2.0-1.0);
    if(dot(normal,normal)<0.01 || encoded.a<0.0) {outOcclusion=vec4(1,-position.z,0,0);return;}
    normal=normalize(normal);
    float fade=1.0-smoothstep(aoDistance.x,aoDistance.y,-position.z);
    if(fade<=0.0) {outOcclusion=vec4(1,0,0,0);return;}
    vec3 view=normalize(-position);
    float radius=aoSampling.x;
    float pixels=min(radius*float(size.y)/(2.0*max(-position.z,0.001)*abs(aoInverseProjection[1][1])),float(max(size.x,size.y)));
    float jitter=fract(dot(vec2(receiver),vec2(0.754877666,0.569840296)));
    float visible=0.0,unoccluded=0.0;
    for(int slice=0;slice<6;slice++) {
        if(slice>=int(aoSampling.z)) break;
        float phi=3.141592654*(float(slice)+jitter)/aoSampling.z;
        vec2 direction=vec2(cos(phi),sin(phi));
        vec3 tangent=VgeAoPosition(uv+direction/vec2(size),depth)-position;
        tangent=normalize(tangent-view*dot(tangent,view));
        vec3 axis=cross(view,tangent);
        vec3 projectedNormal=normal-axis*dot(normal,axis);
        float normalLength=length(projectedNormal);
        if(normalLength<0.0001) continue;
        float angle=atan(dot(projectedNormal,tangent),dot(projectedNormal,view));
        angle=clamp(angle,-1.570796327,1.570796327);
        float low=angle-1.570796327,high=angle+1.570796327;
        float horizonLow=low,horizonHigh=high;
        for(int step=0;step<6;step++) {
            if(step>=int(aoSampling.w)) break;
            float t=(float(step)+0.5)/aoSampling.w;
            float offset=max(1.0,pixels*t*t);
            for(int side=-1;side<=1;side+=2) {
                vec2 sampleUv=uv+float(side)*direction*offset/vec2(size);
                if(any(lessThan(sampleUv,vec2(0)))||any(greaterThanEqual(sampleUv,vec2(1)))) continue;
                sampleUv=(floor(sampleUv*vec2(size))+0.5)/vec2(size);
                float sd=VgeAoDepth(sampleUv,offset/aoSampling.w,-position.z);
                if(sd>=1.0) continue;
                vec3 delta=VgeAoPosition(sampleUv,sd)-position;
                float distanceSquared=dot(delta,delta);
                if(distanceSquared<0.000001 || distanceSquared>=radius*radius) continue;
                vec3 toSample=delta*inversesqrt(distanceSquared);
                // Reject coplanar/back-facing evidence rather than darkening an unoccluded plane.
                if(dot(normal,toSample)<=0.02) continue;
                if(texture(surfaceImage,vec3(sampleUv,VGE_SURFACE_NORMAL)).a<0.0) continue;
                float transmission=clamp(texture(surfaceImage,vec3(sampleUv,VGE_SURFACE_MATERIAL)).a,0.0,1.0);
                float weight=(1.0-distanceSquared/(radius*radius))*(1.0-transmission);
                // Isolated depth spikes have uncertain thickness; continuous walls keep their horizon evidence.
                weight*=VgeAoThickness(sampleUv,direction,-(position.z+delta.z));
                float h=acos(clamp(dot(toSample,view),-1.0,1.0));
                if(side>0) horizonHigh=min(horizonHigh,mix(high,clamp(h,low,high),weight));
                else horizonLow=max(horizonLow,mix(low,clamp(-h,low,high),weight));
            }
        }
        visible+=normalLength*VgeAoIntegral(horizonLow,horizonHigh,angle);
        unoccluded+=normalLength*VgeAoIntegral(low,high,angle);
    }
    float visibility=unoccluded>0.0001?visible/unoccluded:1.0;
    outOcclusion=vec4(mix(1.0,visibility,fade),-position.z,VgeAoEncodeNormal(normal));
}
