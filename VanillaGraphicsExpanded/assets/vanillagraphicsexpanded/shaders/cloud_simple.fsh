#version 330 core
#extension GL_ARB_separate_shader_objects : require
@import "./includes/cloud_simple_parameters.glsl"
@import "./includes/atmosphere_aerial.glsl"
@import "./includes/pbr_scene_storage.glsl"
@import "./includes/pbr_color.glsl"
@import "./includes/oit.glsl"
layout(location=0) in vec3 cloudPosition;
layout(location=1) in vec4 cloudColorCoverage;
layout(location=2) flat in int cloudFace;
layout(location=3) in float cloudThin;
uniform sampler2D liquidDepth;

/** Preserves native adjusted OIT depth without changing its bucket/weight convention. */
float CloudHalfSmooth(float x) { return x>500.0?x-250.0:x*x*x*(1.0-x/1000.0)/250000.0; }
/** Preserves the native normalized liquid-depth measure for shore smoothing. */
float CloudLinearDepth(float z) { return 2.0*vgeFrame.clipPlanes.x/(vgeFrame.clipPlanes.y+vgeFrame.clipPlanes.x-z*(vgeFrame.clipPlanes.y-vgeFrame.clipPlanes.x)); }
/** Evaluates local underwater fog separately from the completed aerial transport. */
float CloudLocalFog()
{
    float depth=length(cloudPosition), clamped=min(250.0,depth);
    float extra=max(-cloudFog.z*clamped*cloudFog.w/60.0,0.0);
    float amount=max(1.0-exp(-(cloudPosition.y-cloudFog.w)*cloudFog.z),1.0-exp(-clamped*cloudFog.x-extra));
    amount=max(min(.04,amount),amount-clamp((8.0-depth)/8.0,0.0,.9));
    return clamp(amount+cloudFog.y,0.0,1.0);
}
/** Lights a cheap two-sided diffuse cloud shell, transports it once, then writes native Simple OIT. */
void main()
{
    if(!VgeFrameLightingValid(vgeFrame.frameIndex,VgeLightingCalibrationGeneration())) discard;
    float alpha=clamp(cloudColorCoverage.a/(cloudColorCoverage.a+.5)*1.5,0.0,1.0);
    if(alpha<.005) discard;
    vec3 normal=normalize(cross(dFdx(cloudPosition),dFdy(cloudPosition)));
    vec3 radiance=cloudColorCoverage.rgb*(cloudEnvironment.rgb+
        (cloudSolar.rgb*abs(dot(normal,cloudSun.xyz))+cloudLunar.rgb*abs(dot(normal,cloudMoon.xyz)))/3.14159265359);
    float localFog=CloudLocalFog();
    if(cloudAerial.z>.7) radiance=mix(radiance,VgeCompatibilityLuminance(cloudFogColor.rgb),localFog);
    else radiance=VgeApplyAerial(radiance,cloudPosition,1.0,cloudAerial.xyw,cloudSun.xyz);
    vec2 uv=gl_FragCoord.xy/vgeFrame.screenSize;
    float liquid=texture(liquidDepth,uv).r;
    if(liquid<gl_FragCoord.z)
    {
        float murk=0.0;
        if(cloudAerial.z<=.7)
            murk=1.0-(CloudLinearDepth(liquid)+CloudLinearDepth(texture(liquidDepth,uv+vec2(0,3)/vgeFrame.screenSize).r)
                +CloudLinearDepth(texture(liquidDepth,uv+vec2(0,6)/vgeFrame.screenSize).r))/3.0;
        radiance=mix(radiance,VgeCompatibilityLuminance(cloudMurk.rgb)*.4,clamp(murk-14.0*cloudFog.x,0.0,1.0));
    }
    // Encode RGB once before alpha/bucket weighting; dimensionless metadata remains unencoded.
    vec4 color=vec4(VgeStoreSceneLighting(radiance),alpha);
    float depth=max(0.0,CloudHalfSmooth((gl_FragCoord.z*2.0-1.0)/gl_FragCoord.w));
    writeOit(color,0.0,depth);
    // Approximate exponential/reciprocal implementations can cross a unit endpoint
    // by one ULP. Transmission remains dimensionless and within its physical domain.
    outOitRevealBins=clamp(outOitRevealBins,0.0,1.0);
    outRevealage=clamp(outRevealage,0.0,1.0);
    // Red is cloud bloom coverage metadata; physical bloom extraction owns the color.
    outOitGlow=vec4(max(0.0,.25-localFog/2.0),0,0,clamp(alpha*5.0-(cloudFace>=5?cloudThin:0.0),0.0,1.0));
}
