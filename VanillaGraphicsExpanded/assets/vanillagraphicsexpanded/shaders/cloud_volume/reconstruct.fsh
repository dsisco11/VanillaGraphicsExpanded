#version 430 core
@import "../includes/oit.glsl"
@import "../includes/vge_frame_ubo.glsl"
@import "../includes/pbr_scene_storage.glsl"
#if VGE_CLOUD_TEMPORAL_DIAGNOSTICS
@import "../includes/cloud_temporal_parameters.glsl"
uniform sampler2D reconstructDiagnostics;
#endif
uniform sampler2D reconstructCloud;
uniform sampler2D reconstructBins;
uniform sampler2D reconstructMetadata;
uniform sampler2D reconstructMaterialDepth;
uniform sampler2D reconstructVisibilityDepth;
uniform sampler2D reconstructLiquidDepth;
/** Filters compact integrated results, then expands them into native products for composition once. */
void main()
{
    vec2 uv=gl_FragCoord.xy/vgeFrame.screenSize;
    #if VGE_CLOUD_TEMPORAL_DIAGNOSTICS
    if(cloudHistory.y>=5)
    {
        vec4 diagnostic=textureLod(reconstructDiagnostics,uv,0);
        vec3 color=vec3(0);
        if(cloudHistory.y==5)color=diagnostic.y==1?vec3(0,1,0):diagnostic.y==2?vec3(0,.4,1):diagnostic.y==3?vec3(1,.5,0):vec3(1,0,0);
        if(cloudHistory.y==6)color=vec3(diagnostic.x);
        if(cloudHistory.y==7)color=diagnostic.z==0?vec3(0,.5,0):vec3(fract(diagnostic.z*.37),fract(diagnostic.z*.61),fract(diagnostic.z*.83));
        if(cloudHistory.y==8)color=vec3(diagnostic.w/7);
        vec4 display=vec4(VgeStoreSceneLighting(color),1);
        outOitRevealBins=vec3(0);outRevealage=0;outOitGlow=vec4(0,0,0,1);
        outOitAccumulation0=display;outOitAccumulation1=display;outOitAccumulation2=display;return;
    }
    #endif
    float depth=min(textureLod(reconstructMaterialDepth,uv,0).r,
        min(textureLod(reconstructVisibilityDepth,uv,0).r,textureLod(reconstructLiquidDepth,uv,0).r));
    ivec2 size=textureSize(reconstructMetadata,0),base=ivec2(floor(uv*vec2(size)-.5));
    bool filtered=true,found=false,empty=true,selectedEmpty=false;float nearest=3.402823e38;vec2 selected=uv;
    // Exact metadata gates continuous filtering. At discontinuities, select a compatible texel;
    // do not reproduce bilinear filtering with four manual fetches of each continuous field.
    for(int y=0;y<2;y++)for(int x=0;x<2;x++)
    {
        ivec2 p=clamp(base+ivec2(x,y),ivec2(0),size-1);
        vec2 metadata=texelFetch(reconstructMetadata,p,0).rg;
        bool compatible=metadata.y>=1&&metadata.x<=depth;
        filtered=filtered&&compatible;
        empty=empty&&metadata.y==2;
        vec2 centre=(vec2(p)+.5)/vec2(size);float distance=dot(centre-uv,centre-uv);
        if(compatible&&distance<nearest){found=true;nearest=distance;selected=centre;selectedEmpty=metadata.y==2;}
    }
    // Certified zero-weight rays are exact identity products. Avoid both continuous samples and
    // native blend writes when the complete support, or selected fallback, is empty.
    if(!found||empty||(!filtered&&selectedEmpty))discard;
    vec2 sampleUv=filtered?uv:selected;
    vec4 cloud=textureLod(reconstructCloud,sampleUv,0);
    cloud.rgb=VgeStoreSceneLighting(cloud.rgb);
    vec3 bins=textureLod(reconstructBins,sampleUv,0).rgb;
    // Expansion follows filtering by design; no native accumulation or glow images are retained.
    outOitRevealBins=bins;outRevealage=clamp(1-cloud.a,0,1);
    outOitGlow=vec4(0,0,0,cloud.a);
    outOitAccumulation0=cloud*(1-bins.x);
    outOitAccumulation1=cloud*(1-bins.y);
    outOitAccumulation2=cloud*(1-bins.z);
}
