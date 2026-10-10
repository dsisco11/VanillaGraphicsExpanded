#version 330 core
layout(location=0) out float outDepth;
uniform sampler2D hzbDepth;
@import "../includes/depth_hierarchy_params.glsl"
/** Conservatively covers the entire proportional footprint, including odd edges and one-texel axes. */
void main() {
    ivec2 sourceSize=textureSize(hzbDepth,srcMip);
    ivec2 destinationSize=max(sourceSize/2,ivec2(1));
    ivec2 destination=ivec2(gl_FragCoord.xy);
    ivec2 first=destination*sourceSize/destinationSize;
    ivec2 end=((destination+1)*sourceSize+destinationSize-1)/destinationSize;
    float nearest=1.0;
    for(int y=0;y<3;y++) for(int x=0;x<3;x++) {
        ivec2 p=first+ivec2(x,y);
        if(all(lessThan(p,end))) nearest=min(nearest,texelFetch(hzbDepth,p,srcMip).r);
    }
    outDepth=nearest;
}
