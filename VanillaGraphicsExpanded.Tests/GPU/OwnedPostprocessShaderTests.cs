using System.Numerics;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR.Postprocessing;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Rendering.Shaders;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks the production bloom and shaft binaries with independent energy and visibility expectations.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class OwnedPostprocessShaderTests(HeadlessGLFixture fixture) : LumOnShaderFunctionalTestBase(fixture)
{
    private PostprocessDraw? draw;

    #region Public API
    /// <summary>Exposure-relative extraction has a soft threshold and normalized reconstruction preserves constant radiance.</summary>
    [Fact]
    public void BloomThresholdAndConstantEnergyAreDefined()
    {
        EnsureShaderTestAvailable();
        var shader = Programs.Create<BloomShaderProgram>();
        using var source = TestFramework.CreateTexture(1, 1, PixelInternalFormat.Rgba32f, [4f,4f,4f,.3f]);
        using var secondary = TestFramework.CreateTexture(1, 1, PixelInternalFormat.Rgba32f, [8f,8f,8f,1f]);
        using var target = TestFramework.CreateTestGBuffer(1, 1, PixelInternalFormat.Rgba32f);
        shader.SourceImage=source; shader.SecondaryImage=secondary;
        shader.Capture(new(0,0,0,0),new(2,.5f,.1f,1)); Draw(shader,target);
        Assert.InRange(target[0].ReadPixels()[0],.29999f,.30001f);
        shader.Capture(new(0,0,1,0),Vector4.Zero); Draw(shader,target);
        Assert.Equal(4,target[0].ReadPixels()[0]);
        shader.Capture(new(0,0,2,0),Vector4.Zero); Draw(shader,target);
        Assert.Equal(6,target[0].ReadPixels()[0]);
        using var dim = TestFramework.CreateTexture(1,1,PixelInternalFormat.Rgba32f,[.5f,.5f,.5f,1f]);
        shader.SourceImage=dim;
        shader.Capture(Vector4.Zero,new(1,.5f,1,0)); Draw(shader,target);
        Assert.Equal(0,target[0].ReadPixels()[0]);
        using var knee = TestFramework.CreateTexture(1,1,PixelInternalFormat.Rgba32f,[1f,1f,1f,1f]);
        shader.SourceImage=knee; Draw(shader,target);
        Assert.InRange(target[0].ReadPixels()[0],.12499f,.12501f);
        Assert.Equal(new[]{4f,4f,4f,.3f},source.ReadPixels());
    }

    /// <summary>Multiresolution reconstruction spreads an HDR impulse into a symmetric graded halo.</summary>
    [Fact]
    public void BloomPyramidProducesSpatialHaloOnNonSquareViewport()
    {
        EnsureShaderTestAvailable();
        var shader=Programs.Create<BloomShaderProgram>();
        float[] pixels=new float[33*17*4];
        for(int i=0;i<33*17;i++) pixels[i*4+3]=1;
        for(int channel=0;channel<3;channel++) pixels[(8*33+16)*4+channel]=64;
        using var source=TestFramework.CreateTexture(33,17,PixelInternalFormat.Rgba32f,pixels);
        using var high=TestFramework.CreateTestGBuffer(17,9,PixelInternalFormat.Rgba32f);
        using var low=TestFramework.CreateTestGBuffer(9,5,PixelInternalFormat.Rgba32f);
        using var result=TestFramework.CreateTestGBuffer(17,9,PixelInternalFormat.Rgba32f);
        shader.SourceImage=source; shader.SecondaryImage=source;
        shader.Capture(Vector4.Zero,new(1,.5f,1,0)); Draw(shader,high);
        float[] narrow=high[0].ReadPixels();
        shader.SourceImage=high[0]; shader.Capture(new(0,0,1,0),Vector4.Zero); Draw(shader,low);
        shader.SourceImage=high[0]; shader.SecondaryImage=low[0];
        shader.Capture(new(0,0,2,0),Vector4.Zero); Draw(shader,result);
        float[] broad=result[0].ReadPixels();
        Assert.True(broad[(4*17+8)*4]>1);
        Assert.True(broad.Where((v,i)=>i%4==0&&v>.0001f).Count()>narrow.Where((v,i)=>i%4==0&&v>.0001f).Count());
        Assert.All(broad,value=>Assert.True(float.IsFinite(value)&&value>=0));
        for(int y=0;y<9;y++) for(int x=0;x<17;x++)
        {
            float value=broad[(y*17+x)*4];
            Assert.InRange(broad[(y*17+16-x)*4],value-.0002f,value+.0002f);
            Assert.InRange(broad[((8-y)*17+x)*4],value-.0002f,value+.0002f);
        }
    }

    #endregion

    #region Private
    /// <summary>Submits the production procedural triangle with deterministic raster state after any readback.</summary>
    private void Draw(GpuProgram program,GpuFramebuffer target)
    {
        draw??=new PostprocessDraw();
        var pipeline=draw.Prepare(program,target);
        Assert.True(GraphicsCommandContext.TryRun("Tests.Postprocess",[pipeline],true,
            commands=>draw.Submit(commands,pipeline,target)));
        Assert.Equal(ErrorCode.NoError,GL.GetError());
    }
    #endregion

    #region Protected API
    /// <summary>Retires the retained graphics pipeline and geometry before the test shader owners.</summary>
    protected override void Dispose(bool disposing)
    {
        if(disposing){draw?.Dispose();draw=null;}
        base.Dispose(disposing);
    }
    #endregion
}
