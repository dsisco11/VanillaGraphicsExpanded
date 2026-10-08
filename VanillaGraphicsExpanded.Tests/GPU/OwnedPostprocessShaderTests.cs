using System.Numerics;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR.Postprocessing;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Shaders;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks the production bloom and shaft binaries with independent energy and visibility expectations.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class OwnedPostprocessShaderTests(HeadlessGLFixture fixture) : LumOnShaderFunctionalTestBase(fixture)
{
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

    /// <summary>Visibility is bounded, occluders and night suppress shafts, and offscreen traversal cannot clamp streaks to the edge.</summary>
    [Theory]
    [InlineData(1f,1f,1f,.5f,.5f,1f)]
    [InlineData(1000f,1f,1f,.5f,.5f,1f)]
    [InlineData(1f,.5f,1f,.5f,.5f,0f)]
    [InlineData(1f,1f,0f,.5f,.5f,0f)]
    [InlineData(1f,1f,1f,100f,100f,0f)]
    public void ShaftsIntegrateOnlyBoundedVisibleSky(float visibility,float depth,float daylight,float sunX,float sunY,float expected)
    {
        EnsureShaderTestAvailable();
        var shader=Programs.Create<GodRayShaderProgram>();
        using var mask=TestFramework.CreateTexture(1,1,PixelInternalFormat.Rgba32f,[0f,visibility,0f,1f]);
        using var depths=TestFramework.CreateTexture(1,1,PixelInternalFormat.R32f,[depth]);
        using var target=TestFramework.CreateTestGBuffer(1,1,PixelInternalFormat.Rgba32f);
        shader.VisibilityImage=mask; shader.DepthImage=depths;
        shader.Capture(Vector4.Zero,new(32,1,0,0),new(sunX,sunY,daylight,0),new(1,.5f,.25f,0));
        Draw(shader,target);
        float[] result=target[0].ReadPixels();
        Assert.InRange(result[0],expected-.00001f,expected+.00001f);
        Assert.InRange(result[1],expected*.5f-.00001f,expected*.5f+.00001f);
        Assert.Equal(1,result[3]);
    }
    #endregion

    #region Private
    /// <summary>Submits the production procedural triangle with deterministic raster state after any readback.</summary>
    private static void Draw(GpuProgram program,GpuFramebuffer target)
    {
        target.BindWithViewport();
        GL.Disable(EnableCap.DepthTest); GL.Disable(EnableCap.Blend); GL.Disable(EnableCap.CullFace);
        GL.Disable(EnableCap.ScissorTest); GL.Disable(EnableCap.FramebufferSrgb); GL.ColorMask(true,true,true,true);
        StateCache.Current.InvalidateAll();
        using var vao=GpuVao.Create();
        using(program.UseScope()) using(vao.BindScope()) GL.DrawArrays(PrimitiveType.Triangles,0,3);
        Assert.Equal(ErrorCode.NoError,GL.GetError());
    }
    #endregion
}
