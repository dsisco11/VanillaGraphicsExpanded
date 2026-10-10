#version 330 core
@import "./includes/ambient_occlusion.glsl"
@import "./includes/gbuffer_layers.glsl"
uniform sampler2D depthImage;
uniform sampler2DArray surfaceImage;
uniform sampler2D depthHierarchy;
layout(location=0) out vec4 outOcclusion;
/** Samples a footprint-sized minimum, then locates its real source receiver to preserve geometry and material identity. */
float VgeAoDepth(inout vec2 uv, float footprint) {
    ivec2 baseSize=textureSize(depthHierarchy,0);
    int maximum=int(floor(log2(float(max(baseSize.x,baseSize.y)))));
    int level=clamp(int(floor(log2(max(footprint,1.0)))),0,min(maximum,4));
    ivec2 size=textureSize(depthHierarchy,level);
    ivec2 pixel=min(ivec2(uv*vec2(size)),size-1);
    float nearest=texelFetch(depthHierarchy,pixel,level).r;
    if(nearest>=1.0) return 1.0;
    // Coarse nearest depth has no source coordinate. Follow conservative proportional footprints
    // to one matching leaf; tie-break by distance to the requested sample for spatial stability.
    for(int iteration=0;iteration<4;iteration++) {
        if(level==0) break;
        ivec2 childSize=textureSize(depthHierarchy,level-1);
        ivec2 first=pixel*childSize/size;
        ivec2 end=((pixel+1)*childSize+size-1)/size;
        ivec2 selected=first;
        float bestDepth=1.0,bestDistance=1e20;
        for(int y=0;y<3;y++) for(int x=0;x<3;x++) {
            ivec2 child=first+ivec2(x,y);
            if(any(greaterThanEqual(child,end))) continue;
            float depth=texelFetch(depthHierarchy,child,level-1).r;
            vec2 delta=(vec2(child)+0.5)/vec2(childSize)-uv;
            float distanceSquared=dot(delta,delta);
            if(depth<bestDepth || (depth==bestDepth && distanceSquared<bestDistance)) {
                bestDepth=depth;bestDistance=distanceSquared;selected=child;
            }
        }
        pixel=selected;size=childSize;nearest=bestDepth;level--;
    }
    uv=(vec2(pixel)+0.5)/vec2(baseSize);
    return nearest;
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
    vec3 normal=mat3(vgeFrame.viewMatrix)*(encoded.xyz*2.0-1.0);
    if(dot(normal,normal)<0.01 || encoded.a<0.0) {outOcclusion=vec4(1,-position.z,0,0);return;}
    normal=normalize(normal);
    float fade=1.0-smoothstep(aoDistance.x,aoDistance.y,-position.z);
    if(fade<=0.0) {outOcclusion=vec4(1,0,0,0);return;}
    vec3 view=normalize(-position);
    float radius=aoSampling.x;
    float pixels=min(radius*float(size.y)/(2.0*max(-position.z,0.001)*abs(vgeFrame.invProjectionMatrix[1][1])),float(max(size.x,size.y)));
    float rotation=fract(dot(vec2(receiver),vec2(0.754877666,0.569840296)));
    float startOffset=fract(dot(vec2(receiver),vec2(0.438289,0.819173)));
    float visible=0.0,unoccluded=0.0;
    for(int slice=0;slice<6;slice++) {
        if(slice>=int(aoSampling.z)) break;
        float phi=3.141592654*(float(slice)+rotation)/aoSampling.z;
        vec2 direction=vec2(cos(phi),sin(phi));
        vec3 tangent=VgeAoPosition(uv+direction/vec2(size),depth)-position;
        tangent=normalize(tangent-view*dot(tangent,view));
        vec3 axis=cross(view,tangent);
        vec3 projectedNormal=normal-axis*dot(normal,axis);
        float normalLength=length(projectedNormal);
        if(normalLength<0.0001) continue;
        float angle=atan(dot(projectedNormal,tangent),dot(projectedNormal,view));
        angle=clamp(angle,-1.570796327,1.570796327);
        float low=max(-1.570796327,angle-1.570796327);
        float high=min(1.570796327,angle+1.570796327);
        float horizonLow=low,horizonHigh=high;
        for(int step=0;step<6;step++) {
            if(step>=int(aoSampling.w)) break;
            float spacing=max(pixels-1.0,0.0)/aoSampling.w;
            float offset=1.0+(float(step)+startOffset)*spacing;
            for(int side=-1;side<=1;side+=2) {
                vec2 sampleUv=uv+float(side)*direction*offset/vec2(size);
                if(any(lessThan(sampleUv,vec2(0)))||any(greaterThanEqual(sampleUv,vec2(1)))) continue;
                sampleUv=(floor(sampleUv*vec2(size))+0.5)/vec2(size);
                float sd=VgeAoDepth(sampleUv,spacing);
                if(sd>=1.0) continue;
                vec3 delta=VgeAoPosition(sampleUv,sd)-position;
                float distanceSquared=dot(delta,delta);
                if(distanceSquared<0.000001 || distanceSquared>=radius*radius) continue;
                vec3 toSample=delta*inversesqrt(distanceSquared);
                // Reject coplanar/back-facing evidence rather than darkening an unoccluded plane.
                if(dot(normal,toSample)<=0.02) continue;
                if(texture(surfaceImage,vec3(sampleUv,VGE_SURFACE_NORMAL)).a<0.0) continue;
                float transmission=clamp(texture(surfaceImage,vec3(sampleUv,VGE_SURFACE_MATERIAL)).a,0.0,1.0);
                float weight=clamp(1.0-distanceSquared/(radius*radius),0.0,1.0)*(1.0-transmission);
                // Isolated depth spikes have uncertain thickness; continuous walls keep their horizon evidence.
                weight*=VgeAoThickness(sampleUv,direction,-(position.z+delta.z));
                // Project the located leaf into this slice; its signed angle can differ from the requested side.
                float h=atan(dot(delta,tangent),dot(delta,view));
                if(h>=0.0) horizonHigh=min(horizonHigh,acos(mix(cos(high),cos(clamp(h,0.0,high)),weight)));
                else horizonLow=max(horizonLow,-acos(mix(cos(low),cos(clamp(-h,0.0,-low)),weight)));
            }
        }
        visible+=normalLength*VgeAoIntegral(horizonLow,horizonHigh,angle);
        unoccluded+=normalLength*VgeAoIntegral(low,high,angle);
    }
    float visibility=unoccluded>0.0001?visible/unoccluded:1.0;
    outOcclusion=vec4(mix(1.0,visibility,fade),-position.z,VgeAoEncodeNormal(normal));
}
