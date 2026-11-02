using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using System.Numerics;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Shaders.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Compares production water optical helpers to independent linear and photon-direction predictions.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class WaterTransportTests(HeadlessGLFixture fixture) : LumOnShaderFunctionalTestBase(fixture)
{


    #region Public API
    /// <summary>HDR radiance survives confidence blending, colored attenuation and entry/exit world-space scattering.</summary>
    [Fact]
    public void LinearCompositionAndWaterPhotonDirectionsMatchReference()
    {
        EnsureShaderTestAvailable();
        var program = Programs.Create<WaterTransportShaderProgram>();
        using var aerial = RHICommandContext.Current.CreateDynamicTexture3D(new RHITextureDescription { Dimension = TextureDimension.Three, Format = TextureFormat.Rgba32f, Width = System.Math.Max(1, 1), Height = System.Math.Max(1, 2), Depth = System.Math.Max(1, 1), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Linear, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        using var attenuation = RHICommandContext.Current.CreateDynamicTexture3D(new RHITextureDescription { Dimension = TextureDimension.Three, Format = TextureFormat.Rgba32f, Width = System.Math.Max(1, 1), Height = System.Math.Max(1, 1), Depth = System.Math.Max(1, 1), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Linear, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        aerial.UploadDataImmediate(new float[] {1,2,3,0, 0,0,0,0},0,0,0,1,2,1,0);
        attenuation.UploadDataImmediate([.2f,.4f,.6f,0],0,0,0,1,1,1,0);
        using var neutralOcclusion = TestFramework.CreateTexture(1, 1, TextureFormat.R32f, [0f]);
        program.LightShaftOcclusion = neutralOcclusion;
        program.AerialRadiance = aerial; program.AerialAttenuation = attenuation;
        using var target = CreateMRTRenderTarget(4, 13, TextureFormat.Rgba32f,TextureFormat.Rgba32f);
        TestFramework.RenderQuadTo(program, target);
        // Independently author the original double intervals; only bounded comparison results return to the CPU.
        var lower=Enumerable.Range(0,2).Select(_=>Enumerable.Repeat(double.NegativeInfinity,4*13*4).ToArray()).ToArray();
        var upper=Enumerable.Range(0,2).Select(_=>Enumerable.Repeat(double.PositiveInfinity,4*13*4).ToArray()).ToArray();
        double[] weights = [0, .25, .5, 1], fallback = [2,4,8], receiver = [16,8,4];
        double[] background = [8,4,2], extinction = [.3,.5,.7], scattering = [.2,.3,.4];
        for (int x = 0; x < 4; x++)
        {
            double alpha = .25 * (1 - weights[x]) + weights[x];
            for (int channel = 0; channel < 3; channel++)
            {
                double expected = (fallback[channel] * .25 * (1 - weights[x]) + receiver[channel] * weights[x]) / alpha;
                Expect(expected,0,x * 4 + channel);
                double transmission = Math.Exp(-extinction[channel] * weights[x] * 10);
                Expect(background[channel] * transmission + (channel + 3) * scattering[channel] / extinction[channel]
                    * (1 - transmission),0,(4 + x) * 4 + channel);
            }
            Expect(alpha,0,x * 4 + 3);
            // Entry follows the reverse refracted ray; exit follows the camera-side water ray.
            float refractedX = .6f / 1.333f;
            Vector3 outgoing = x % 2 == 0 ? new(-refractedX, 0, MathF.Sqrt(1 - refractedX * refractedX)) : new(-.6f, 0, .8f);
            if (x >= 2) outgoing = new(outgoing.Z, outgoing.Y, -outgoing.X);
            for (int channel = 0; channel < 3; channel++) Expect(outgoing[channel],0,(8 + x) * 4 + channel);
            double cosine = Vector3.Dot(-Vector3.Normalize(new(.2f,.8f,-.4f)), outgoing);
            Expect((1 - .7 * .7) / (4 * Math.PI * Math.Pow(1 + .7 * .7 - 2 * .7 * cosine, 1.5)),0,(8 + x) * 4 + 3);
        }
        double[] coverages = [.001,.25,1], visibility = [0,.4,1], loss = [.2,.4,.6];
        for (int row = 3; row < 12; row++)
        for (int x = 0; x < 4; x++)
        {
            double coverage = coverages[(row - 3) / 3], sky = visibility[(row - 3) % 3];
            double alpha = coverage * (1 - weights[x]) + weights[x];
            int offset = (row * 4 + x) * 4;
            // The atmospheric source is weighted once by the resulting coverage,
            // independent of whether either constituent has full transmission.
            for (int channel = 0; channel < 3; channel++)
            {
                double mixture = (fallback[channel] * coverage * (1 - weights[x]) + receiver[channel] * weights[x]) / alpha;
                double expected = mixture * (1 - loss[channel] * sky) + (channel + 1) * sky;
                Expect(expected,0,offset + channel);
                Expect(expected,1,offset + channel);
            }
            Expect(alpha,0,offset + 3); Expect(alpha,1,offset + 3);
        }
        for (int x = 0; x < 4; x++)
        for (int channel = 0; channel < 3; channel++)
        {
            double air = background[channel] * (1 - loss[channel] * weights[x]) + (channel + 1) * weights[x];
            Expect(air * Math.Exp(-.1 * (channel + 1) * 2),0,(12 * 4 + x) * 4 + channel);
        }
        foreach(var selected in new (int Target,int Row,int Rows,uint Mask)[]{(0,0,1,15u),(0,1,1,7u),(0,2,10,15u),(0,12,1,7u),(1,3,9,15u)})
        {
            int first=selected.Row*16,last=first+selected.Rows*16;
            ImageAssertions.Comparisons.CompareIntervals(target[selected.Target],lower[selected.Target][first..last],upper[selected.Target][first..last],
                new(new(0,0),0,selected.Row,0,4,selected.Rows,1),selected.Mask).AssertEqual();
        }

        /// <summary>Retains the original per-component double tolerance without narrowing expected values.</summary>
        void Expect(double expected,int attachment,int component)
        {
            double tolerance=2e-5*Math.Max(1,Math.Abs(expected));
            lower[attachment][component]=expected-tolerance;
            upper[attachment][component]=expected+tolerance;
        }
    }
    #endregion
    #region Private
    /// <summary>Prepares this suite's declared observation programs before subject execution.</summary>
    private protected override void PrepareObservations(VanillaGraphicsExpanded.Tests.GPU.Assertions.GpuObservationPrograms programs)
    {
        base.PrepareObservations(programs);
        programs.Prepare("compare", 0, 0);
    }
    #endregion
}
