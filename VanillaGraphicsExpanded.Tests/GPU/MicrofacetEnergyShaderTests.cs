using System.Numerics;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR.Materials.Energy;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks hardware-filtered half-float lookup interpolation, furnace endpoints and invalid-input containment.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class MicrofacetEnergyShaderTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Public API
    /// <summary>GPU bilinear coefficients and compensation agree with the independently addressed CPU asset.</summary>
    [Theory]
    [InlineData(.04f, 0f)]
    [InlineData(.04f, .000001f)]
    [InlineData(.37f, .003f)]
    [InlineData(.5f, .51f)]
    [InlineData(.97f, .91f)]
    [InlineData(1f, 1f)]
    public void GpuLookupMatchesCpuAndWhiteFurnace(float roughness, float viewCosine)
    {
        EnsureContextValid();
        float[] actual = Render(roughness, viewCosine, false);
        var expected = MicrofacetTextureReference.Sample(MicrofacetEnergyLookup.Prepare().Width,roughness,viewCosine);
        Assert.InRange(Math.Abs(actual[0]-expected.Value.X), 0, expected.Error.X);
        Assert.InRange(Math.Abs(actual[1]-expected.Value.Y), 0, expected.Error.Y);
        TestContext.Current.TestOutputHelper!.WriteLine($"size={MicrofacetEnergyLookup.Prepare().Width} r={roughness:R} nv={viewCosine:R} ideal={expected.Value} halfOutput={expected.Filtered} GPU=({actual[0]:R},{actual[1]:R})");
        Assert.InRange(actual[2], .999999f, 1.000001f);
        Assert.Equal(1, actual[3]);
    }

    /// <summary>A nonfinite material disables the lobe instead of injecting NaNs into scene storage.</summary>
    [Fact]
    public void InvalidMaterialFailsClosed()
    {
        EnsureContextValid();
        float[] actual = Render(.5f, .5f, true);
        Assert.All(actual, value => Assert.Equal(0, value));
    }

    /// <summary>Integrating the actual shader BRDF over a uniform hemisphere restores a white conductor's missing energy.</summary>
    [Theory]
    [InlineData(.25f)]
    [InlineData(.5f)]
    [InlineData(1f)]
    public void ActualCompensatedBrdfPassesNormalWhiteFurnace(float roughness)
    {
        EnsureContextValid();
        float[] actual = Render(roughness, 1, false, furnace: true);
        for (int channel=0; channel<3; channel++) Assert.InRange(actual[channel], .998f, 1.002f);
        Assert.Equal(1, actual[3]);
    }

    /// <summary>Uniform environment reflection and diffuse budgets sum to white across dielectric and conductor endpoints.</summary>
    [Theory]
    [InlineData(.04f, .001f, 0f)]
    [InlineData(.5f, .5f, 0f)]
    [InlineData(1f, 1f, 0f)]
    [InlineData(.04f, .001f, 1f)]
    [InlineData(.5f, .5f, 1f)]
    [InlineData(1f, 1f, 1f)]
    public void EnvironmentPartitionPreservesWhiteFurnace(float roughness, float viewCosine, float metallic)
    {
        EnsureContextValid();
        float[] actual = Render(roughness, viewCosine, false, environmentMetallic: metallic);
        for (int channel=0; channel<3; channel++) Assert.InRange(actual[channel], .999998f, 1.000002f);
        Assert.Equal(1, actual[3]);
    }

    /// <summary>Raw float32 grazing peaks follow the canceled Smith equation without an artificial denominator floor.</summary>
    [Theory]
    [InlineData(.0000001f)]
    [InlineData(.000001f)]
    [InlineData(.001f)]
    [InlineData(.1f)]
    [InlineData(.5f)]
    public void SmoothGrazingPeakMatchesAnalyticResponse(float viewCosine)
    {
        EnsureContextValid();
        float[] actual = Render(.04f, viewCosine, false, grazing: true);
        double a2=Math.Pow(.04,4);
        var energy=MicrofacetTextureReference.Sample(MicrofacetEnergyLookup.Prepare().Width,.04f,viewCosine);
        double numerator=1/(4*Math.PI*a2*Math.Sqrt(a2+(1-a2)*viewCosine*viewCosine));
        double expected=numerator/energy.Value.X;
        for(int channel=0;channel<3;channel++) Assert.InRange(actual[channel],
            numerator/(energy.Value.X+energy.Error.X)*.9998,numerator/(energy.Value.X-energy.Error.X)*1.0002);
        TestContext.Current.TestOutputHelper!.WriteLine($"raw compensated grazing: roughness=.04 NdotV={viewCosine:R} expected={expected:R} actual={actual[0]:R}");
    }

    /// <summary>Normalized water surface radiance exposes the smooth grazing peak that its OIT alpha must contain.</summary>
    [Fact]
    public void WaterNormalizedGrazingWitnessRemainsFinite()
    {
        EnsureContextValid();
        float[] actual = Render(.04f, 1e-7f, false, waterAngle: .00002344894967816431f);
        TestContext.Current.TestOutputHelper!.WriteLine($"normalized water grazing: response={actual[0]:R} HdotN={actual[1]:R} Hx={actual[2]:R} LdotN={actual[3]:R}");
        // Nearly opposite directions amplify Float32 cancellation. Evaluate the independent
        // equation at the GPU-recovered half vector instead of imposing the original double geometry.
        const double nv=1e-7,f0=.02037;
        double a2=Math.Pow(.04,4),nh=actual[1],nl=actual[3],vh=actual[2]+nv*nh;
        double denominator=(1-nh*nh)+a2*nh*nh;
        double distribution=a2/(Math.PI*denominator*denominator);
        double visibility=.5/(nl*Math.Sqrt(a2+(1-a2)*nv*nv)+nv*Math.Sqrt(a2+(1-a2)*nl*nl));
        var lookup=MicrofacetTextureReference.Sample(MicrofacetEnergyLookup.Prepare().Width,.04f,(float)nv);
        var energy=lookup.Value;
        double fresnel=f0+(1-f0)*Math.Pow(1-vh,5);
        double expected=distribution*visibility*nl*fresnel/(energy.X*f0+energy.Y*(1-f0));
        // The retained language contract permits reassociation into fma(nh2,a2-1,1).
        // Near the mirror peak its rounded a2-1 differs materially from the stable sum;
        // verify either explicitly evaluated arithmetic schedule rather than widening tolerance.
        float r=.04f,alpha=r*r,alphaSquared=alpha*alpha,nhSquared=actual[1]*actual[1];
        double reassociatedDenominator=MathF.FusedMultiplyAdd(nhSquared,alphaSquared-1,1);
        double reassociated=expected*denominator*denominator/(reassociatedDenominator*reassociatedDenominator);
        double energyDenominator=energy.X*f0+energy.Y*(1-f0);
        double energyError=lookup.Error.X*f0+lookup.Error.Y*(1-f0);
        bool Admitted(double reference)=>actual[0]>=reference*energyDenominator/(energyDenominator+energyError)*.9995
            &&actual[0]<=reference*energyDenominator/(energyDenominator-energyError)*1.0005;
        TestContext.Current.TestOutputHelper!.WriteLine($"stable recovered reference={expected:R}; reassociated Float32 denominator reference={reassociated:R}");
        Assert.True(Admitted(expected)||Admitted(reassociated),"Recovered BRDF arithmetic and the independently bounded texture filter must contain the actual peak.");
        Assert.InRange(actual[0],0,42_000_000f);
        Assert.True(float.IsFinite(actual[0]));
    }

    /// <summary>Multiple callers borrow immutable storage; retirement withdraws it before a replacement is published.</summary>
    [Fact]
    public void LookupReusesAndReplacesOwnedStorage()
    {
        EnsureContextValid();
        var first = MicrofacetEnergyLookup.Prepare();
        Assert.Same(first, MicrofacetEnergyLookup.Prepare());
        Assert.True(first.IsValid);
        MicrofacetEnergyLookup.Retire();
        Assert.False(first.IsValid);
        var replacement = MicrofacetEnergyLookup.Prepare();
        Assert.NotSame(first, replacement);
        Assert.True(replacement.IsValid);
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>Every quality owns RG16F storage and filters endpoints and off-grid coordinates without changing source data.</summary>
    [Theory]
    [InlineData(0,16)] [InlineData(1,32)] [InlineData(2,64)] [InlineData(3,128)]
    public void QualityTextureFiltersDeclaredGrid(int quality,int size)
    {
        EnsureContextValid();
        var config=VanillaGraphicsExpanded.ModSystems.ConfigModSystem.Config;
        int previous=config.ShadingQuality;
        try
        {
            config.ShadingQuality=quality;
            var texture=MicrofacetEnergyLookup.Prepare();
            Assert.Equal(size,texture.Width); Assert.Equal(size,texture.Height);
            Assert.Same(texture,MicrofacetEnergyLookup.Prepare());
            using(var binding=StateCache.Current.BindTextureScope(TextureTarget.Texture2D,0,texture.TextureId))
            {
                GL.GetTexLevelParameter(TextureTarget.Texture2D,0,GetTextureParameter.TextureInternalFormat,out int format);
                Assert.Equal((int)PixelInternalFormat.Rg16f,format);
                GL.GetTexLevelParameter(TextureTarget.Texture2D,1,GetTextureParameter.TextureWidth,out int mipWidth);
                Assert.Equal(0,mipWidth);
            }
            GL.GetSamplerParameter(GpuSamplers.LinearClamp.SamplerId,SamplerParameterName.TextureMinFilter,out int filter);
            Assert.Equal((int)TextureMinFilter.Linear,filter);
            foreach(var coordinate in new[]{(.04f,0f),(.04f,1f),(1f,0f),(1f,1f),(.37f,.003f),(.5f,.51f),(.97f,.91f)})
            {
                var expected=MicrofacetTextureReference.Sample(size,coordinate.Item1,coordinate.Item2);
                var actual=Render(coordinate.Item1,coordinate.Item2,false);
                Assert.InRange(Math.Abs(actual[0]-expected.Value.X),0,expected.Error.X);
                Assert.InRange(Math.Abs(actual[1]-expected.Value.Y),0,expected.Error.Y);
                Assert.InRange(actual[2],.999999f,1.000001f);
            }
            config.ShadingQuality=quality==3?0:3;
            var replacement=MicrofacetEnergyLookup.Prepare();
            Assert.NotSame(texture,replacement); Assert.False(texture.IsValid);
            Assert.True(replacement.IsValid);
            Assert.Equal(ErrorCode.NoError,GL.GetError());
        }
        finally { config.ShadingQuality=previous; MicrofacetEnergyLookup.Prepare(); }
    }
    #endregion

    #region Private
    /// <summary>Renders the selected texture through the real shader helper at the retained draw-language version.</summary>
    private float[] Render(float roughness, float viewCosine, bool invalid, bool furnace = false,
        float environmentMetallic = -1, bool grazing = false, float waterAngle = 0)
    {
        string helper = File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
            "assets/shaders/includes/pbr_microfacet_energy.glsl"));
        string common = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "assets/shaders/includes/pbr_common.glsl"))
            .Replace("@import \"./common_constants.glsl\"", File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
                "assets/shaders/includes/common_constants.glsl")));
        string environment = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "assets/shaders/includes/pbr_environment.glsl"))
            .Replace("@import \"./pbr_common.glsl\"", "").Replace("@import \"./pbr_microfacet_energy.glsl\"", "");
        using var modules = new TerrainShaderTestFixture();
        int vertex = modules.Compile(ShaderType.VertexShader,
            "#version 330 core\nlayout(location=0) in vec2 position;void main(){gl_Position=vec4(position,0,1);}");
        int fragment = modules.Compile(ShaderType.FragmentShader, "#version 330 core\n" + helper + common + environment + """
            uniform float roughness, viewCosine, material;
            uniform float environmentMetallic;
            uniform int furnace;
            uniform int grazing;
            uniform float waterAngle;
            layout(location=0) out vec4 result;
            void main(){
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
            """);
        int program = TerrainShaderTestFixture.Link(vertex, fragment);
        try
        {
            GL.UseProgram(program);
            GL.Uniform1(GL.GetUniformLocation(program, "roughness"), roughness);
            GL.Uniform1(GL.GetUniformLocation(program, "viewCosine"), viewCosine);
            GL.Uniform1(GL.GetUniformLocation(program, "material"), invalid ? float.NaN : 1f);
            GL.Uniform1(GL.GetUniformLocation(program, "furnace"), furnace ? 1 : 0);
            GL.Uniform1(GL.GetUniformLocation(program, "environmentMetallic"), environmentMetallic);
            GL.Uniform1(GL.GetUniformLocation(program, "grazing"), grazing ? 1 : 0);
            GL.Uniform1(GL.GetUniformLocation(program, "waterAngle"), waterAngle);
            MicrofacetTextureReference.Bind();
            using var target = CreateRenderTarget(1, 1, PixelInternalFormat.Rgba32f);
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, target.FboId);
            GL.Viewport(0, 0, 1, 1);
            GL.Disable(EnableCap.DepthTest); GL.Disable(EnableCap.Blend); GL.Disable(EnableCap.CullFace);
            GL.Disable(EnableCap.ScissorTest); GL.ColorMask(true, true, true, true);
            RenderFullscreenQuad(program);
            return ReadPixelsFloat(target);
        }
        finally { GL.DeleteProgram(program); StateCache.Current.InvalidateAll(); }
    }
    #endregion
}
