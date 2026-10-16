#version 330 core
#extension GL_ARB_separate_shader_objects : require
@import "./includes/cloud_simple_parameters.glsl"
@import "./includes/cloud_simple_noise.glsl"
layout(location=0) in vec3 vertexPosition;
layout(location=1) in vec4 rgbaBase;
layout(location=2) in uint nativeFace;
layout(location=3) in vec2 cloudTileOffset;
layout(location=4) in vec4 neibCloudThickness;
layout(location=5) in float selfThickness;
layout(location=6) in float cloudBrightness;
layout(location=7) in float thinCloudModeness;
layout(location=8) in float undulatingModeness;
layout(location=9) in float cloudOpaqueness;
layout(location=0) out vec3 cloudPosition;
layout(location=1) out vec4 cloudColorCoverage;
layout(location=2) flat out int cloudFace;
layout(location=3) out float cloudThin;

/** Retains the native mesh geometry and coverage controls with shader-local perception. */
void main()
{
    int flags=int(nativeFace);
    vec3 p=vertexPosition+vec3(32767.0*cloudTileOffset.x,0,32767.0*cloudTileOffset.y);
    if (cloudPerception.x==2.0 && cloudPerception.y>0.0)
    {
        float intensity=cloudPerception.y*clamp(length(p)/2.0-2.0,0.0,2.0);
        vec2 xz=(p.xz+cloudPlayer.xz)/10.0;
        p+=intensity*.5*vec3(CloudNoise(vec3(xz,cloudPerception.z/6.0)),
            CloudNoise(vec3(xz,cloudPerception.z/10.0)),CloudNoise(vec3(xz,cloudPerception.z/3.5)));
    }
    float height=(selfThickness-.1)*(selfThickness-.1)*(500.0-500.0*thinCloudModeness);
    if(p.y>0.0) p.y+=height*max(0.0,1.0-3.0*thinCloudModeness);
    float side=1.0;
    if(flags<4)
    {
        float neighbor=neibCloudThickness[flags];
        side=clamp(abs(selfThickness-neighbor)+max(0.0,(1.0-neighbor)*(1.0-thinCloudModeness))
            +max(0.0,(selfThickness-.2)*(1.0-thinCloudModeness)),0.0,1.0);
        if(neighbor<=.001) side=1.0;
    }
    float edge=cloudOffsetLength.w/2.0-length(p);
    float fade=min(1.0,edge/(cloudOffsetLength.w*.2));
    float undulate=CloudNoise(vec3((p.x+cloudTileClockAlpha.x)/100.0,(p.z+cloudTileClockAlpha.y)/500.0,cloudTileClockAlpha.z))*undulatingModeness;
    undulate*=min(1.0,(edge+400.0)/cloudOffsetLength.w);
    p.y+=undulate*50.0;
    p.y-=max(length(p.xz+cloudOffsetLength.xz)/10.0-50.0,0.0);
    float coverage=min(1.0,cloudOpaqueness*min(1.0,10.0*selfThickness))*max(0.0,side*fade)+undulatingModeness/2.0;
    // Preserve local sphere coverage removal in the same pre-placement coordinate convention.
    vec3 fogRay=p*10.0; float depth=length(fogRay), fog=0.0;
    vec3 direction=depth>0.0?fogRay/depth:vec3(0,1,0);
    for(int i=0;i<int(cloudPerception.w);i++)
    {
        vec4 sphere=cloudSpheres[2*i]; float density=cloudSpheres[2*i+1].x;
        float t=dot(sphere.xyz,direction), distance2=dot(sphere.xyz,sphere.xyz)-t*t;
        if(length(sphere.xyz)<sphere.w)
        {
            float inside=dot(sphere.xyz,sphere.xyz);
            float chord=t>=0.0?sqrt(max(0.0,sphere.w*sphere.w-inside+t*t)):sqrt(max(0.0,sphere.w*sphere.w-inside));
            fog+=min(chord,depth)*density;
        }
        else if(t>=0.0 && distance2<sphere.w*sphere.w)
        {
            float halfChord=sqrt(max(0.0,sphere.w*sphere.w-distance2));
            float start=max(0.0,t-halfChord), end=min(depth,t+halfChord);
            fog+=max(0.0,end-start)*density;
        }
    }
    coverage*=clamp(1.0-fog*2.0,0.0,1.0);
    if(flags==4) coverage*=.75;
    if(flags==5) coverage*=1.25;
    if(flags<4) coverage*=clamp(max(1.0-50.0*neibCloudThickness[flags],20.0*abs(neibCloudThickness[flags]-selfThickness)),0.0,1.0);
    cloudColorCoverage=vec4(clamp(rgbaBase.rgb,0.0,1.0),cloudTileClockAlpha.w*clamp(coverage,0.0,1.0));
    // Legacy instance brightness is intentionally not incident light.
    cloudFace=flags; cloudThin=thinCloudModeness;
    cloudPosition=p+cloudOffsetLength.xyz;
    gl_Position=cloudProjection*cloudModelView*vec4(p,1);
}
