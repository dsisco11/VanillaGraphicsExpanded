using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using System.Numerics;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR.Materials.Energy;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;
using VanillaGraphicsExpanded.Tests.Support.Lighting;

using VanillaGraphicsExpanded.Tests.Support.GPU.Native;

using VanillaGraphicsExpanded.Tests.Support.GPU.Harness;

namespace VanillaGraphicsExpanded.Tests.Component.PBR.Materials;

/// <summary>Checks hardware-filtered half-float lookup interpolation, furnace endpoints and invalid-input containment.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
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
        // Arrange: Select roughness and view angle, then prepare production energy lookup inputs.
        // Act and assert: Render the GPU lookup and compare it with the independent CPU reference and white-furnace bound.
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
        // Arrange: Supply nonfinite material parameters to the production BRDF lookup.
        // Act and assert: Evaluate the lobe and verify invalid material data contributes no nonfinite radiance.
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
        // Arrange: Select roughness and prepare the actual compensated BRDF under normal-incidence white illumination.
        // Act and assert: Render the furnace and require the integrated response to stay within the energy bound.
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
        // Arrange: Select roughness, view angle, and metallic partition for the environment response.
        // Act and assert: Evaluate the partitioned environment and verify the combined white-furnace response is preserved.
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
        // Arrange: Select grazing view cosine and compute the independent analytic specular peak.
        // Act and assert: Render the production lobe and compare its peak with the analytic response.
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
        // Arrange: Use normalized water surface radiance at grazing incidence with its OIT alpha witness.
        // Act and assert: Evaluate the production water response and require finite radiance within the alpha containment rule.
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

    /// <summary>Every quality owns RG16F storage and filters endpoints and off-grid coordinates without changing source data.</summary>
    [Theory]
    [InlineData(0,16)] [InlineData(1,32)] [InlineData(2,64)] [InlineData(3,128)]
    public void QualityTextureFiltersDeclaredGrid(int quality,int size)
    {
        // Arrange: Select a quality tier and its authored lookup-grid extent.
        // Act and assert: Sample the generated quality texture and verify filtering over the declared grid.
        EnsureContextValid();
        var config=VanillaGraphicsExpanded.ModSystems.ConfigModSystem.Config;
        int previous=config.ShadingQuality;
        try
        {
            config.ShadingQuality=quality;
            var texture=MicrofacetEnergyLookup.Prepare();
            Assert.Equal(size,texture.Width); Assert.Equal(size,texture.Height);
            Assert.Same(texture,MicrofacetEnergyLookup.Prepare());
            using(var binding=OpenGlStateCache.Current.BindTextureScope(TextureTarget.Texture2D,0,texture.TextureId))
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
    /// <summary>Submits the declared microfacet probe with the requested numeric inputs and reads its one-pixel output.</summary>
    private float[] Render(float roughness, float viewCosine, bool invalid, bool furnace = false,
        float environmentMetallic = -1, bool grazing = false, float waterAngle = 0)
    {
        using var programs = new ComponentShaderPrograms();
        var program = programs.Create<MicrofacetEnergyProbeProgram>();
        program.EnergyLookup = MicrofacetEnergyLookup.Prepare();
        program.EnergySampler = GpuSamplers.LinearClamp;
        program.Capture(roughness, viewCosine, invalid ? float.NaN : 1f, furnace ? 1 : 0,
            environmentMetallic, grazing ? 1 : 0, waterAngle);

        var context = RHICommandContext.Current;
        var layout = new RHIVertexLayout([
            new(0, 2, VertexComponentFormat.Float, VertexInterpretation.Floating, 0, 0, 4 * sizeof(float)),
            new(1, 2, VertexComponentFormat.Float, VertexInterpretation.Floating, 0, 2 * sizeof(float), 4 * sizeof(float))]);
        using var vertices = context.CreateVertexBuffer(new RHIBufferDescription
        {
            Role = BufferRole.Vertex,
            Mode = ResourceStorageMode.Mutable,
            Capacity = 0,
            Usage = BufferUsage.StaticDraw,
            Label = "Tests.MicrofacetEnergy.Quad"
        }).RequireResource();
        vertices.UploadData(new float[]
        {
            -1.0f, -1.0f, 0.0f, 0.0f,
             1.0f, -1.0f, 1.0f, 0.0f,
             1.0f,  1.0f, 1.0f, 1.0f,
            -1.0f, -1.0f, 0.0f, 0.0f,
             1.0f,  1.0f, 1.0f, 1.0f,
            -1.0f,  1.0f, 0.0f, 1.0f
        });
        using var geometry = new ArrayGraphicsGeometry(layout, PrimitiveTopology.Triangles,
            new Dictionary<int, GpuVbo> { [0] = vertices });
        using var target = CreateRenderTarget(1, 1, TextureFormat.Rgba32f);
        var pass = FramebufferTestSetup.DescribeAttachments(target,
            [new(AttachmentIndex: 0, Load: AttachmentLoad.Clear, Clear: ColorClearValue.Float(0, 0, 0, 0))]);
        var renderTargets = context.DescribeRenderTargets(pass);
        using var pipeline = context.CreateGraphicsPipeline(new(program.Shader, layout, renderTargets,
            DynamicPipelineState.Viewport)).RequireResource();
        using var boundary = context.EnterBoundary(new("Tests.MicrofacetEnergyProbe", true,
            graphics: [pipeline], operations: BoundaryOperations.RenderPasses));
        boundary.Run(() =>
        {
            context.BeginRenderPass(pass);
            context.SetPipeline(pipeline);
            context.SetDynamicState(new() { Viewport = context.PassViewport });
            context.BindResources(program.CaptureBindings());
            context.Draw(geometry.Inputs, new(0, 6));
            context.EndRenderPass();
        });
        return ReadPixelsFloat(target);
    }
    #endregion
}
