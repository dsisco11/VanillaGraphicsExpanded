#version 450 core
@import "vanillagraphicsexpanded:shaders/includes/post_local_exposure.glsl"
@import "vanillagraphicsexpanded:shaders/includes/pbr_color.glsl"
@import "vanillagraphicsexpanded:shaders/includes/post_filmic.glsl"
layout(std140, binding=28) uniform LocalExposureProbeInputs
{
    vec4 parameters;
    vec4 strengthsAndBounds;
    int operation;
};
uniform sampler2D sourceImage;
uniform sampler2D baseImage;
uniform sampler3D gradingLookup;
layout(location=0) out vec4 result;

/** Selects a direct observation while leaving all numerical behavior in the production helper include. */
void main()
{
    ivec2 pixel=ivec2(gl_FragCoord.xy);
    if(operation==1)
    {
        float base;
        bool valid=VgeSampleLocalBase(baseImage,parameters.xy,parameters.z,parameters.w,base);
        result=vec4(base,valid?1.0:0.0,0.0,1.0);
    }
    else if(operation==2)
    {
        float coordinate=texelFetch(sourceImage,pixel,0).r;
        float base;
        bool valid=VgeSampleLocalBase(baseImage,vec2(coordinate),parameters.x,parameters.y,base);
        result=vec4(base,valid?1.0:0.0,0.0,1.0);
    }
    else if(operation==3)
    {
        float value=texelFetch(sourceImage,pixel,0).r;
        result=vec4(VgeLocalExposureStops(value,parameters.x,parameters.y,
            strengthsAndBounds.xy,strengthsAndBounds.zw),0.0,0.0,1.0);
    }
    else if(operation==4)
    {
        float value=texelFetch(sourceImage,pixel,0).r;
        result=vec4(VgeLocalExposureStops(value,0.0,0.0,vec2(1.0),vec2(8.0)),
            VgeLocalExposureStops(0.0,value,0.0,vec2(0.5),vec2(8.0)),0.0,1.0);
    }
    else if(operation==5)
    {
        float pivot=texelFetch(sourceImage,pixel,0).r;
        result=vec4(VgeLocalExposureStops(-12.0,2.0,pivot,vec2(0.5),vec2(4.0)),0.0,0.0,1.0);
    }
    else if(operation==6)
    {
        float value=texelFetch(sourceImage,pixel,0).r;
        float correction=VgeLocalExposureStops(value,parameters.x,parameters.y,
            strengthsAndBounds.xy,strengthsAndBounds.zw);
        result=vec4(correction,value+correction,value+0.25+correction,1.0);
    }
    else if(operation==7)
    {
        result=vec4(VgeLocalExposureStops(-100.0,0.0,0.0,vec2(0.0),vec2(100.0)),
            VgeLocalExposureStops(100.0,0.0,0.0,vec2(0.0),vec2(100.0)),0.0,1.0);
    }
    else if(operation==8)
    {
        result=vec4(VgeFilterLocalBase(sourceImage,ivec2(parameters.xy),parameters.z),0.0,1.0);
    }
    else if(operation==9)
    {
        vec3 color=texelFetch(sourceImage,pixel,0).rgb/parameters.x;
        float guidance,base;
        bool valid=VgeLocalLogLuminance(color,guidance)
            && VgeSampleLocalBase(baseImage,gl_FragCoord.xy/vec2(textureSize(sourceImage,0)),
                guidance,parameters.z,base);
        float correction=valid ? VgeLocalExposureStops(base,parameters.y,0.0,
            strengthsAndBounds.xy,strengthsAndBounds.zw) : 0.0;
        result=vec4(valid ? color*exp2(correction) : vec3(0.0),correction);
    }
    else if(operation==10)
    {
        vec3 color=texelFetch(sourceImage,pixel,0).rgb*parameters.x;
        float size=float(textureSize(gradingLookup,0).x);
        vec3 coordinate=VgeGradingCoordinate(color);
        result=vec4(texture(gradingLookup,(coordinate*(size-1.0)+0.5)/size).rgb,1.0);
    }
    else
    {
        result=vec4(0.0);
    }
}
