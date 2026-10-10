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
        VerifyComposition(lumon,pbr,.5f,1f,0f,0f,1f,1f);
    }

    /// <summary>Checks split material response, actual view angle, neutral/full/partial AO and independent strengths in both lighting modes.</summary>
    [Theory]
    [InlineData(false,false)]
    [InlineData(true,false)]
    [InlineData(true,true)]
    public void DiffuseAndSpecularVisibilityRespectMaterialAndView(bool lumon,bool pbr)
    {
        foreach(float roughness in new[]{0f,.05f,.5f,1f})
        foreach(float nDotV in new[]{.1f,.5f,1f}) {
            VerifyComposition(lumon,pbr,roughness,nDotV,0f,.35f,1f,1f);
            VerifyComposition(lumon,pbr,roughness,nDotV,1f,.35f,1f,1f);
        }
        VerifyComposition(lumon,pbr,.05f,.1f,1f,1f,1f,1f);
        VerifyComposition(lumon,pbr,.05f,.1f,0f,.35f,0f,1f);
        VerifyComposition(lumon,pbr,.05f,.1f,0f,.35f,1f,0f);
        VerifyComposition(lumon,pbr,.05f,.1f,0f,.35f,0f,0f);
        VerifyComposition(lumon,pbr,.05f,.1f,0f,.35f,.5f,.25f);
        VerifyComposition(lumon,pbr,.05f,.1f,1f,.35f,1f,1f,offAxis:true);
        VerifyComposition(lumon,pbr,.05f,.1f,0f,.35f,-1f,2f);
    }
    #endregion

    #region Private
    /// <summary>Renders known direct/emissive and indirect energies and compares their split response before exposure or transport.</summary>
    private void VerifyComposition(bool lumon,bool pbr,float roughness,float nDotV,float metallic,float ao,float diffuseStrength,float specularStrength,bool offAxis=false)
    {
        EnsureShaderTestAvailable();
        var shader = Programs.Create<PBRCompositeShaderProgram>(p => { p.LumOnEnabled = lumon; p.EnablePbrComposite = pbr; p.EnableShortRangeAo = false; });
        using var direct = TestFramework.CreateTexture(1,1,PixelInternalFormat.Rgba32f,[1f,1f,1f,1f]);
        using var specular = TestFramework.CreateTexture(1,1,PixelInternalFormat.Rgba32f,[2f,2f,2f,1f]);
        using var emission = TestFramework.CreateTexture(1,1,PixelInternalFormat.Rgba32f,[3f,3f,3f,1f]);
        using var indirect = TestFramework.CreateTexture(1,1,PixelInternalFormat.Rgba32f,[2f,2f,2f,1f]);
        using var albedo = TestFramework.CreateTexture(1,1,PixelInternalFormat.Rgba32f,[.5f,.5f,.5f,1f]);
        float encodedX=offAxis?.5f:.5f+.5f*MathF.Sqrt(1-nDotV*nDotV);
        float encodedZ=offAxis?1f:.5f+.5f*nDotV;
        float receiverX=offAxis?2*MathF.Sqrt(1-nDotV*nDotV)/nDotV:0;
        using var normal = TestFramework.CreateTexture(1,1,PixelInternalFormat.Rgba32f,[encodedX,.5f,encodedZ,1f]);
        using var proxyNormal = TestFramework.CreateTexture(1,1,PixelInternalFormat.Rgba32f,[encodedX,.5f,encodedZ,-1f]);
        using var material = TestFramework.CreateTexture(1,1,PixelInternalFormat.Rgba32f,[roughness,metallic,0f,0f]);
        using var position = TestFramework.CreateTexture(1,1,PixelInternalFormat.Rgba32f,[receiverX,0f,-2f,1f]);
        using var depth = TestFramework.CreateTexture(1,1,PixelInternalFormat.R32f,[.75f]);
        using var visibility = TestFramework.CreateTexture(1,1,PixelInternalFormat.Rgba32f,[ao,2f,0f,0f]);
        using var wrongDepth = TestFramework.CreateTexture(1,1,PixelInternalFormat.Rgba32f,[ao,20f,0f,0f]);
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
        using var frameCamera = TestFrameCamera.Create([1,0,0,0,0,1,0,0,0,0,-4,0,receiverX,0,0,1],
            [1,0,0,0,0,1,0,0,0,0,1,0,0,0,0,1]);
        shader.FrameInputs = frameCamera;

        shader.IndirectTint=new(1,1,1);shader.IndirectIntensity=1;shader.DiffuseAOStrength=diffuseStrength;shader.SpecularAOStrength=specularStrength;
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
        Assert.InRange(baseline[0],6f,8f);
        shader.SetAmbientOcclusion(visibility);
        // Subsequent sibling-field writes must preserve the AO publication flag.
        shader.DiffuseAOStrength=diffuseStrength;shader.SpecularAOStrength=specularStrength;
        float[] dark=Render();
        // Independent scalar material evaluation isolates direct/emissive radiance from the two indirect terms.
        double f0=.04*(1-metallic)+.5*metallic;
        double fresnel=f0+(1-f0)*Math.Pow(1-nDotV,5);
        double diffuse=lumon&&!pbr?1-metallic:(1-fresnel)*(1-metallic);
        double spec=lumon&&!pbr?0:2*fresnel*(1-roughness);
        double diffuseVisibility=1+(ao-1)*Math.Clamp(diffuseStrength,0,1);
        double derivedSpecular=Math.Clamp(Math.Pow(nDotV+ao,Math.Pow(2,-16*roughness-1))-1+ao,0,1);
        double specularVisibility=1+(derivedSpecular-1)*Math.Clamp(specularStrength,0,1);
        double expected=6+diffuse*diffuseVisibility+spec*specularVisibility;
        double expectedBaseline=6+diffuse+spec;
        for(int c=0;c<3;c++) {
            Assert.InRange(baseline[c],expectedBaseline-.001,expectedBaseline+.001);
            Assert.InRange(dark[c],expected-.001,expected+.001);
        }
        shader.SetAmbientOcclusion(wrongDepth);float[] unmatched=Render();
        for(int c=0;c<3;c++)Assert.InRange(unmatched[c],baseline[c]-.001f,baseline[c]+.001f);
        shader.GBufferSurface=proxySurface;shader.SetAmbientOcclusion(visibility);float[] proxy=Render();
        for(int c=0;c<3;c++)Assert.InRange(proxy[c],baseline[c]-.001f,baseline[c]+.001f);
        Assert.Equal(ErrorCode.NoError,GL.GetError());
    }
    #endregion
}
