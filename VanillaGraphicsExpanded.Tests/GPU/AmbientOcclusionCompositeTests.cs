using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR;
using VanillaGraphicsExpanded.PBR.Postprocessing;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;
using VanillaGraphicsExpanded.Rendering.Pipeline.Passes;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Constrains ambient visibility to indirect transport at a matched world receiver.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class AmbientOcclusionCompositeTests(HeadlessGLFixture fixture) : LumOnShaderFunctionalTestBase(fixture)
{
    #region Public API
    /// <summary>Occlusion suppresses only ambient lighting; mismatched depth and first-person receiver metadata bypass it.</summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void VisibilityAffectsOnlyMatchedIndirectLighting(bool lumon, bool pbr)
    {
        EnsureShaderTestAvailable();
        var shader = Programs.Create<PBRCompositeShaderProgram>(p => { p.LumOnEnabled = lumon; p.EnablePbrComposite = pbr; p.EnableShortRangeAo = false; });
        using var direct = TestFramework.CreateTexture(1,1,PixelInternalFormat.Rgba32f,[1f,1f,1f,1f]);
        using var specular = TestFramework.CreateTexture(1,1,PixelInternalFormat.Rgba32f,[2f,2f,2f,1f]);
        using var emission = TestFramework.CreateTexture(1,1,PixelInternalFormat.Rgba32f,[3f,3f,3f,1f]);
        using var indirect = TestFramework.CreateTexture(1,1,PixelInternalFormat.Rgba32f,[2f,2f,2f,1f]);
        using var albedo = TestFramework.CreateTexture(1,1,PixelInternalFormat.Rgba32f,[.5f,.5f,.5f,1f]);
        using var normal = TestFramework.CreateTexture(1,1,PixelInternalFormat.Rgba32f,[.5f,.5f,1f,1f]);
        using var proxyNormal = TestFramework.CreateTexture(1,1,PixelInternalFormat.Rgba32f,[.5f,.5f,1f,-1f]);
        using var material = TestFramework.CreateTexture(1,1,PixelInternalFormat.Rgba32f,[.5f,0f,0f,0f]);
        using var position = TestFramework.CreateTexture(1,1,PixelInternalFormat.Rgba32f,[0f,0f,-2f,1f]);
        using var depth = TestFramework.CreateTexture(1,1,PixelInternalFormat.R32f,[.75f]);
        using var visibility = TestFramework.CreateTexture(1,1,PixelInternalFormat.Rgba32f,[0f,2f,0f,0f]);
        using var wrongDepth = TestFramework.CreateTexture(1,1,PixelInternalFormat.Rgba32f,[0f,20f,0f,0f]);
        using var lighting = LayeredTestTexture.Create(direct,specular,emission);
        using var surface = LayeredTestTexture.Create(normal,material,indirect);
        using var proxySurface = LayeredTestTexture.Create(proxyNormal,material,indirect);
        using var target = TestFramework.CreateTestGBuffer(1,1,PixelInternalFormat.Rgba32f,3);
        using var positions = GpuVbo.Create(); positions.UploadData(new float[] {-1,-1,0, 3,-1,0, -1,3,0});
        using var uv = GpuVbo.Create(); uv.UploadData(new float[] {0,0, 2,0, 0,2});
        using var geometry = new ArrayGraphicsGeometry(EngineFullscreenGeometry.Layout, PrimitiveType.Triangles,
            new Dictionary<int,GpuVbo> { [0]=positions, [1]=uv });
        using var lifetime = new GraphicsPipelineLifetime();
        var passDescription = new RenderPassDesc(target,[new(0),new(1),new(2)]);
        using var pass = new RenderPassTargets(passDescription);
        shader.DirectLighting=lighting;shader.IndirectDiffuse=indirect;shader.GBufferAlbedo=albedo.TextureId;
        shader.GBufferSurface=surface;shader.GBufferPosition=position.TextureId;shader.PrimaryDepth=depth.TextureId;
        shader.InvProjectionMatrix=[1,0,0,0,0,1,0,0,0,0,-4,0,0,0,0,1];
        shader.ViewMatrix=[1,0,0,0,0,1,0,0,0,0,1,0,0,0,0,1];
        shader.IndirectTint=new(1,1,1);shader.IndirectIntensity=1;shader.DiffuseAOStrength=1;shader.SpecularAOStrength=1;
        shader.SetAtmosphere(null);shader.SetWaterVolume(null);shader.SetUnderwater(false);
        using var pipeline = new GraphicsPipeline(lifetime,new(shader.GraphicsIdentity!,EngineFullscreenGeometry.Layout,pass.Signature,DynamicPipelineState.Viewport),shader);
        /// <summary>Publishes prepared inputs and reads only after the graphics boundary restores state.</summary>
        float[] Render()
        {
            Assert.True(GraphicsCommandContext.TryRun("Tests.AmbientComposition",[pipeline],true,commands=> {
                commands.BeginPass(passDescription);commands.SetPipeline(pipeline);
                commands.SetDynamicState(new(){Viewport=commands.PassViewport});commands.Draw(geometry,new(0,3));commands.EndPass();
            }));
            return target[0].ReadPixels();
        }
        shader.SetAmbientOcclusion(null);float[] baseline=Render();
        Assert.True(baseline[0]>6.01f);
        shader.SetAmbientOcclusion(visibility);
        // Subsequent sibling-field writes must preserve the AO publication flag.
        shader.DiffuseAOStrength=1;shader.SpecularAOStrength=1;
        float[] dark=Render();
        // Existing PBR specular occlusion is roughness-weighted: 2 * F0(.04) * (1-r) * residual(r).
        float retainedSpecular = lumon && pbr ? 2f * .04f * .5f * .5f : 0;
        for(int c=0;c<3;c++)Assert.InRange(dark[c],6f+retainedSpecular-.001f,6f+retainedSpecular+.001f);
        shader.SetAmbientOcclusion(wrongDepth);float[] unmatched=Render();
        for(int c=0;c<3;c++)Assert.InRange(unmatched[c],baseline[c]-.001f,baseline[c]+.001f);
        shader.GBufferSurface=proxySurface;shader.SetAmbientOcclusion(visibility);float[] proxy=Render();
        for(int c=0;c<3;c++)Assert.InRange(proxy[c],baseline[c]-.001f,baseline[c]+.001f);
        Assert.Equal(ErrorCode.NoError,GL.GetError());
    }
    #endregion
}
