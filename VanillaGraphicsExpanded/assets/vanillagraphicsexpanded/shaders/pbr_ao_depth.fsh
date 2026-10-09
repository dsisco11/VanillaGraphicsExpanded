#version 330 core
layout(std140) uniform PostprocessInputs { vec4 pass; vec4 effect; vec4 sun; vec4 solar; };
uniform sampler2D sourceImage;
layout(location=0) out vec2 outDepthRange;
/** Reduces valid 2x2 footprints, including the last row/column of odd-sized inputs. */
void main() {
    ivec2 start=ivec2(gl_FragCoord.xy)*2, size=textureSize(sourceImage,0);
    vec2 range=vec2(1.0,0.0);
    for(int y=0;y<2;y++) for(int x=0;x<2;x++) {
        ivec2 p=start+ivec2(x,y);
        if(any(greaterThanEqual(p,size))) continue;
        vec2 d=texelFetch(sourceImage,p,0).rg;
        if(pass.z<0.5) d.y=d.x;
        range=vec2(min(range.x,d.x),max(range.y,d.y));
    }
    outDepthRange=range;
}
