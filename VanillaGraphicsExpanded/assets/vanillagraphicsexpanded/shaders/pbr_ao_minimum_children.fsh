#version 330 core
uniform sampler2D depthHierarchy;
layout(std140) uniform AoMinimumChildMaskInputs { ivec4 maskParameters; };
layout(location=0) out uint outColor;
/** Retains every minimum child in conservative proportional footprints, in original row-major order. */
void main() {
    int level=maskParameters.x;
    ivec2 size=textureSize(depthHierarchy,level), childSize=textureSize(depthHierarchy,level-1);
    ivec2 pixel=ivec2(gl_FragCoord.xy);
    ivec2 first=pixel*childSize/size;
    ivec2 end=((pixel+1)*childSize+size-1)/size;
    float nearest=texelFetch(depthHierarchy,pixel,level).r;
    uint bits=0u;
    for(int y=0;y<3;y++) for(int x=0;x<3;x++) {
        ivec2 child=first+ivec2(x,y);
        if(any(greaterThanEqual(child,end))) continue;
        if(texelFetch(depthHierarchy,child,level-1).r==nearest) bits|=1u<<uint(y*3+x);
    }
    outColor=bits;
}

