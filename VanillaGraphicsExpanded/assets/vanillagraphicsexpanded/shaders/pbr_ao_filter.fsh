#version 330 core
@import "./includes/ambient_occlusion.glsl"
@import "./includes/gbuffer_layers.glsl"
uniform sampler2D sourceImage;
uniform sampler2D depthImage;
uniform sampler2DArray surfaceImage;
layout(location=0) out vec4 outOcclusion;
/** Applies a joint depth/normal filter, then reconstructs full-resolution visibility from matched receivers. */
void main() {
    ivec2 sourceSize=textureSize(sourceImage,0);
    vec2 uv=gl_FragCoord.xy/(aoFrame.w<0.5?vec2(sourceSize):vgeFrame.screenSize);
    ivec2 center=clamp(ivec2(uv*vec2(sourceSize)),ivec2(0),sourceSize-1);
    vec4 receiver=texelFetch(sourceImage,center,0);
    vec3 normal=VgeAoDecodeNormal(receiver.ba);
    if(aoFrame.w>0.5) {
        float d=texture(depthImage,uv).r;
        vec4 encoded=texture(surfaceImage,vec3(uv,VGE_SURFACE_NORMAL));
        if(d>=1.0 || encoded.a<0.0 || dot(encoded.xyz*2.0-1.0,encoded.xyz*2.0-1.0)<0.01) {
            outOcclusion=vec4(1,0,0,0);return;
        }
        float receiverDepth=-VgeAoPosition(uv,d).z;
        if(receiverDepth>=aoDistance.y) {outOcclusion=vec4(1,0,0,0);return;}
        normal=normalize(mat3(vgeFrame.viewMatrix)*(encoded.xyz*2.0-1.0));
        receiver=vec4(1,receiverDepth,VgeAoEncodeNormal(normal));
    }
    if(receiver.g<=0.0) {outOcclusion=vec4(1,0,0,0);return;}
    float sum=0.0,weights=0.0;
    for(int y=-1;y<=1;y++) for(int x=-1;x<=1;x++) {
        ivec2 pixel=center+ivec2(x,y);
        if(any(lessThan(pixel,ivec2(0)))||any(greaterThanEqual(pixel,sourceSize))) continue;
        vec4 sampleValue=texelFetch(sourceImage,pixel,0);
        if(sampleValue.g<=0.0) continue;
        float dz=abs(sampleValue.g-receiver.g);
        float tolerance=0.02+0.01*receiver.g;
        if(dz>3.0*tolerance) continue;
        float agreement=max(0.0,dot(normal,VgeAoDecodeNormal(sampleValue.ba)));
        vec2 delta=(vec2(pixel)+0.5)-uv*vec2(sourceSize);
        float weight=exp(-dot(delta,delta)*0.5-dz/tolerance)*pow(agreement,32.0);
        sum+=sampleValue.r*weight;weights+=weight;
    }
    // Unmatched thin receivers are neutral; a neighboring wall must not supply their visibility.
    float visibility=weights>0.0001?sum/weights:1.0;
    if(aoFrame.w>0.5) visibility=mix(visibility,1.0,clamp(texture(surfaceImage,vec3(uv,VGE_SURFACE_MATERIAL)).a,0.0,1.0));
    outOcclusion=vec4(visibility,receiver.g,receiver.ba);
}
