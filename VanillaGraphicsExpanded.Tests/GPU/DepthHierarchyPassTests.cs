using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.PBR.Postprocessing;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
namespace VanillaGraphicsExpanded.Tests.GPU;
/// <summary>Checks production conservative mip generation, feedback-safe access and resource retirement.</summary>
[Collection("GPU")]
[Trait("Category","GPU")]
public sealed class DepthHierarchyPassTests(HeadlessGLFixture fixture) : LumOnShaderFunctionalTestBase(fixture)
{
    #region Public API
    /// <summary>Every source texel, including odd edges and one-dimensional tails, contributes to its proportional parent footprint.</summary>
    [Theory]
    [InlineData(65,37)]
    [InlineData(7,3)]
    [InlineData(1,17)]
    [InlineData(17,1)]
    [InlineData(1,1)]
    [InlineData(4,4)]
    public void EveryMipMatchesIndependentConservativeReduction(int width,int height) {
        EnsureShaderTestAvailable();
        using var owner=new DepthHierarchyPass();using var draw=new PostprocessDraw();
        var copy=Programs.Create<DepthHierarchyCopyShaderProgram>();var reduce=Programs.Create<DepthHierarchyDownsampleShaderProgram>();
        float[] values=Enumerable.Range(0,width*height).Select(i=>.1f+.8f*((i*31)%101)/101f).ToArray();
        values[^1]=.001f;
        using var source=TestFramework.CreateTexture(width,height,PixelInternalFormat.R32f,values);
        var pipelines=owner.Prepare(draw,width,height,copy,reduce);
        using(var hostile=new HostileFullscreenState()) {
            Assert.True(GraphicsCommandContext.TryRun("Tests.DepthHierarchy",pipelines,true,commands=>owner.Render(commands,draw,source)));
            hostile.AssertRestored();
        }
        var texture=owner.Texture!;long bytes=0;int w=width,h=height;
        for(int level=0;level<texture.MipLevels;level++) {
            Assert.Equal(values,texture.ReadPixels(level));bytes+=4L*w*h;
            if(level+1==texture.MipLevels)break;
            int nextW=Math.Max(1,w/2),nextH=Math.Max(1,h/2);
            float[] expected=new float[nextW*nextH];
            // A parent's normalized interval overlaps these source intervals. This reference uses
            // interval intersection rather than the shader's integer footprint implementation.
            for(int y=0;y<nextH;y++)for(int x=0;x<nextW;x++) {
                float minimum=1;
                for(int sy=0;sy<h;sy++)for(int sx=0;sx<w;sx++)
                    if((long)sx*nextW<(long)(x+1)*w && (long)(sx+1)*nextW>(long)x*w &&
                       (long)sy*nextH<(long)(y+1)*h && (long)(sy+1)*nextH>(long)y*h)
                        minimum=Math.Min(minimum,values[sy*w+sx]);
                expected[y*nextW+x]=minimum;
            }
            values=expected;w=nextW;h=nextH;
        }
        Assert.Equal(.001f,values[0]);Assert.Equal(bytes,owner.StorageBytes);
        // Restored mip accessibility is observable through readback of the complete chain above.
        Assert.Same(texture,owner.Texture);
        owner.Prepare(draw,width,height,copy,reduce);Assert.Same(texture,owner.Texture);
        owner.Prepare(draw,width+2,height+2,copy,reduce);Assert.False(texture.IsValid);
        owner.Dispose();Assert.Null(owner.Texture);Assert.Equal(0,owner.StorageBytes);
        Assert.Equal(ErrorCode.NoError,GL.GetError());
    }
    #endregion
}
