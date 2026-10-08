using System.Collections.Immutable;
using System.Numerics;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.ModSystems;
using VanillaGraphicsExpanded.PBR.Atmosphere;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.PBR.Postprocessing;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Executes finite-path lookup against independent angular and distance ramps.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class AtmosphereAerialLookupTests(HeadlessGLFixture fixture) : LumOnShaderFunctionalTestBase(fixture)
{
    #region Lookup reconstruction
    /// <summary>Physical distance, angular direction, identity and enclosure gates select the intended volume coordinates.</summary>
    [Fact]
    public void SamplesAngularDistanceGridAndPreservesIdentity()
    {
        EnsureShaderTestAvailable();
        var program=Programs.Create<AerialLookupProgram>();
        using var framework = new ShaderTestFramework();
        using var target = framework.CreateTestGBuffer(1, 1, PixelInternalFormat.Rgba32f);
        using var owner = new AtmosphereModSystem();
        const int width = 4, height = 5, depth = 24;
        float[] scatter = new float[width * height * depth * 4], loss = new float[scatter.Length], mie = new float[scatter.Length];
        for (int z = 0; z < depth; z++)
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            int offset = ((z * height + y) * width + x) * 4;
            scatter[offset] = (float)z / (depth - 1); scatter[offset + 1] = (float)y / (height - 1);
            scatter[offset + 2] = (float)x / width; scatter[offset + 3] = loss[offset + 3] = 1;
            float angular = AtmosphereMieTransport.Factor(AtmosphereMieTransport.Direction(x, y, width, height, 0).Y);
            for (int c = 0; c < 3; c++) { mie[offset + c] = .05f; scatter[offset + c] += .05f * angular; }
            mie[offset + 3] = 1;
        }
        owner.Publish(new(Vector3.UnitY, Vector3.Zero, Vector3.Zero, Vector3.Zero, Vector3.Zero,
            ImmutableArray.CreateRange(new float[width * height * 4]))
        { Width = width, Height = height, AerialRadiance = ImmutableArray.CreateRange(scatter), AerialAttenuation = ImmutableArray.CreateRange(loss), AerialMie = ImmutableArray.CreateRange(mie) });
        using var neutral=framework.CreateTexture(1,1,PixelInternalFormat.R32f,[0f]);
        program.Radiance=AtmosphereModSystem.AerialRadianceTexture;program.Attenuation=AtmosphereModSystem.AerialAttenuationTexture;program.Occlusion=neutral;
        using var draw=new PostprocessDraw();
        foreach (float row in new[] { .25f, .5f, .75f })
        foreach (float slice in new[] { .2f, .8f })
        foreach (float visibility in new[] { 0f, 1f })
        foreach (float azimuth in new[] { 0f, MathF.PI / 2 })
        {
            float elevation = AtmosphereSkyMapping.Elevation(row, 0);
            float distance = MathF.Exp(MathF.Log(2500001f) * slice) - 1;
            Vector3 direction = new(MathF.Cos(elevation) * MathF.Cos(azimuth), MathF.Sin(elevation), MathF.Cos(elevation) * MathF.Sin(azimuth));
            program.Capture(direction*distance,visibility);
            var pipeline=draw.Prepare(program,target);
            Assert.True(GraphicsCommandContext.TryRun("Tests.Aerial",[pipeline],true,commands=>draw.Submit(commands,pipeline,target)));
            float[] actual = target[0].ReadPixels();
            float lobe = .05f * AtmosphereMieTransport.Factor(direction.Y) * visibility;
            for (int c = 0; c < 3; c++) actual[c] -= lobe;
            Assert.InRange(MathF.Abs(actual[0] - (1 + slice * visibility)), 0, .001f);
            Assert.InRange(MathF.Abs(actual[1] - (1 + row * visibility)), 0, .001f);
            Assert.InRange(MathF.Abs(actual[2] - (1 + (azimuth == 0 ? .375f : .125f) * visibility)), 0, .001f);
        }
        program.Capture(Vector3.Zero,1);
        var identityPipeline=draw.Prepare(program,target);
        Assert.True(GraphicsCommandContext.TryRun("Tests.AerialIdentity",[identityPipeline],true,commands=>draw.Submit(commands,identityPipeline,target)));
        Assert.Equal(new[] { 1f, 1f, 1f, 1f }, target[0].ReadPixels());
    }
    /// <summary>Spatial shaft occlusion removes atmospheric in-scattering without changing surface transmission or enclosure identity.</summary>
    [Fact]
    public void LightShaftOcclusionModulatesOnlyAerialScattering()
    {
        EnsureShaderTestAvailable();var program=Programs.Create<AerialLookupProgram>();
        using var framework=new ShaderTestFramework();
        using var target=framework.CreateTestGBuffer(2,1,PixelInternalFormat.Rgba32f);
        using var mask=framework.CreateTexture(2,1,PixelInternalFormat.R32f,[0f,1f]);
        using var owner=new AtmosphereModSystem();const int width=4,height=5,depth=24;
        float[] scatter=Enumerable.Repeat(new[]{2f,1f,.5f,1f},width*height*depth).SelectMany(x=>x).ToArray();
        float[] loss=Enumerable.Repeat(new[]{.4f,.4f,.4f,1f},width*height*depth).SelectMany(x=>x).ToArray();
        owner.Publish(new(Vector3.UnitY,Vector3.Zero,Vector3.Zero,Vector3.Zero,Vector3.Zero,ImmutableArray.CreateRange(new float[width*height*4]))
        {Width=width,Height=height,AerialRadiance=ImmutableArray.CreateRange(scatter),AerialAttenuation=ImmutableArray.CreateRange(loss),AerialMie=ImmutableArray.CreateRange(new float[scatter.Length])});
        program.Radiance=AtmosphereModSystem.AerialRadianceTexture;program.Attenuation=AtmosphereModSystem.AerialAttenuationTexture;program.Occlusion=mask;
        using var draw=new PostprocessDraw();
        foreach(float availability in new[]{1f,0f})
        {
            program.Capture(new(100,0,0),availability);
            var pipeline=draw.Prepare(program,target);
            Assert.True(GraphicsCommandContext.TryRun("Tests.AerialOcclusion",[pipeline],true,commands=>draw.Submit(commands,pipeline,target)));
            float[] actual=target[0].ReadPixels();
            float[] expected=availability>0?new[]{2.6f,1.6f,1.1f,1f,.6f,.6f,.6f,1f}:Enumerable.Repeat(1f,8).ToArray();
            for(int i=0;i<8;i++)Assert.InRange(actual[i],expected[i]-.002f,expected[i]+.002f);
        }
        Assert.Equal(ErrorCode.NoError,GL.GetError());
    }
    #endregion
}
