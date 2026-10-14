#version 330 core
#extension GL_ARB_separate_shader_objects : require
@import "./includes/vge_frame_ubo.glsl"
@import "./includes/pbr_scene_storage.glsl"
layout(std140,binding=28) uniform MoonInputs
{
    vec4 moonCentreRadius;
    vec4 moonRightHorizon;
    vec4 moonUp;
    vec4 moonSurfaceLight;
    vec4 moonRadiance;
};
layout(location=0) out vec4 outColor;
layout(location=1) out vec4 outGlow;
/** Evaluates a phased Lambert disk calibrated on the CPU to the incident lunar source. */
void main()
{
    vec2 ndc=gl_FragCoord.xy/vgeFrame.screenSize*2.0-1.0;
    vec4 a=vgeFrame.invCurrViewProjMatrix*vec4(ndc,-1,1);
    vec4 b=vgeFrame.invCurrViewProjMatrix*vec4(ndc,0,1);
    vec3 direction=normalize(b.xyz/b.w-a.xyz/a.w);
    vec2 disk=vec2(dot(direction,moonRightHorizon.xyz),dot(direction,moonUp.xyz))/sin(moonCentreRadius.w);
    float radius2=dot(disk,disk);
    float width=max(fwidth(radius2),1e-6);
    float coverage=1.0-smoothstep(1.0-width*.5,1.0+width*.5,radius2);
    if(dot(direction,moonCentreRadius.xyz)<=0.0 || direction.y<sin(moonRightHorizon.w) || coverage<=0.0) discard;
    // Edge antialiasing may cover a pixel just outside the limb; keep its normal on the unit hemisphere.
    vec3 surface=vec3(disk/max(1.0,sqrt(radius2)),sqrt(max(0.0,1.0-radius2)));
    float illumination=max(dot(surface,moonSurfaceLight.xyz),0.0);
    // The source already includes external transmission. Add only its normalized surface response.
    outColor=vec4(VgeStoreSceneLighting(moonRadiance.rgb*(illumination*coverage)),0.0);
    outGlow=vec4(0.0);
}
