#version 330 core
@import "../includes/pbr_microfacet_energy.glsl"
@import "../includes/pbr_common.glsl"
@import "../includes/pbr_environment.glsl"

layout(std140, binding = 28) uniform MicrofacetEnergyInputs
{
    float roughness;
    float viewCosine;
    float material;
    int furnace;
    float environmentMetallic;
    int grazing;
    float waterAngle;
    int coordinateMode;
};

layout(binding = 0) uniform sampler2D lookupCoordinates;
layout(location = 0) out vec4 result;

/** Evaluates the existing lookup, furnace, environment, grazing, and water diagnostics. */
void main()
{
    if (coordinateMode != 0)
    {
        vec2 coordinate = texelFetch(lookupCoordinates, ivec2(int(gl_FragCoord.x), 0), 0).rg;
        result = vec4(VgeDirectionalAlbedo(coordinate.x, coordinate.y), 0, 1);
        return;
    }

    VgeLobeEnergy lobe=VgeEvaluateMicrofacetEnergy(vec3(material),roughness,viewCosine);
    result=lobe.valid ? vec4(VgeDirectionalAlbedo(roughness,viewCosine),lobe.reflectance.x,1) : vec4(0);
    if(furnace!=0){
        vec3 integral=vec3(0);
        // Normal view is azimuth-invariant, so this integrates the actual shader's
        // BRDF directly over projected incident cosine without half-vector sampling.
        for(int i=0;i<8192;i++){
            float nl=(float(i)+.5)/8192.0;
            vec3 light=vec3(sqrt(1.0-nl*nl),0,nl);
            integral+=cookTorranceBRDF(vec3(0,0,1),vec3(0,0,1),light,vec3(1),roughness)*nl;
        }
        result=vec4(integral*(2.0*3.141592653589793/8192.0)*lobe.compensation,1);
    }
    if(environmentMetallic>=0.0){
        vec3 diffuse,specular;
        VgeEnvironmentSplit(vec3(1),vec3(1),environmentMetallic,roughness,viewCosine,diffuse,specular);
        result=vec4(diffuse+specular,1);
    }
    if(grazing!=0){
        vec3 view=vec3(sqrt(1.0-viewCosine*viewCosine),0,viewCosine);
        vec3 light=vec3(-view.x,0,view.z);
        result=vec4(cookTorranceBRDF(vec3(0,0,1),view,light,vec3(1),roughness)*lobe.compensation*viewCosine,1);
    }
    if(waterAngle>0.0){
        vec3 view=vec3(sqrt(1.0-viewCosine*viewCosine),0,viewCosine);
        vec3 halfVector=vec3(sin(waterAngle),0,cos(waterAngle));
        vec3 light=2.0*dot(view,halfVector)*halfVector-view;
        VgeLobeEnergy water=VgeEvaluateMicrofacetEnergy(vec3(.02037),roughness,viewCosine);
        float response=cookTorranceBRDF(vec3(0,0,1),view,light,vec3(.02037),roughness).x
            *water.compensation.x*max(light.z,0.0)/max(water.reflectance.x,.001);
        vec3 recoveredHalf=normalize(view+light);
        result=vec4(response,recoveredHalf.z,recoveredHalf.x,light.z);
    }
}
