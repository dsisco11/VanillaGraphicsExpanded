using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Tests.Support.GPU.Native;
using VanillaGraphicsExpanded.Rendering.RHI;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.PBR.Postprocessing;
using VanillaGraphicsExpanded.Tests.Support.GPU.Observations;
using Assertions = VanillaGraphicsExpanded.Tests.Support.GPU.Observations;

namespace VanillaGraphicsExpanded.Tests.Component.Postprocessing;
/// <summary>Checks production conservative mip generation, feedback-safe access and resource retirement.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class DepthHierarchyPassTests(HeadlessGLFixture fixture) : LumOnShaderFunctionalTestBase(fixture)
{


    #region Public API
    /// <summary>Every source texel, including odd edges and one-dimensional tails, contributes to its proportional parent footprint.</summary>
    [Theory]
    [InlineData(65,37)]
    [InlineData(129,127)]
    [InlineData(257,131)]
    [InlineData(1,257)]
    [InlineData(257,1)]
    [InlineData(7,3)]
    [InlineData(1,17)]
    [InlineData(17,1)]
    [InlineData(1,1)]
    [InlineData(4,4)]
    public void EveryMipMatchesIndependentConservativeReduction(int width,int height) {
        // Arrange: Fill every source texel deterministically and place a unique minimum at the final source edge.
        EnsureShaderTestAvailable();
        using var owner=new DepthHierarchyPass();using var draw=new PostprocessDraw();
        var compute=Programs.CreateDepthHierarchy();
        float[] values=Enumerable.Range(0,width*height).Select(i=>.1f+.8f*((i*31)%101)/101f).ToArray();
        values[^1]=.001f;
        using var source=TestFramework.CreateTexture(width,height,TextureFormat.R32f,values);
        owner.Prepare(width,height,compute);
        using(var hostile=new HostileFullscreenState()) {
        // Act: Render the complete production mip chain while hostile caller state is active.
        owner.Render(source, record: width % 2 != 0);
            hostile.AssertRestored();
        }
        // Assert: Compare every mip against interval-overlap reduction, then verify reuse, resize retirement, and disposal.
        var texture=owner.Texture!;long bytes=0;int w=width,h=height;
        var assertions = ImageAssertions;
        for(int level=0;level<texture.MipLevels;level++) {
            assertions.Comparisons.Compare(texture, values, selection: new(new(level,0),0,0,0,w,h,1)).AssertEqual();bytes+=4L*w*h;
            if(level+1==texture.MipLevels)break;
            int nextW=Math.Max(1,w/2),nextH=Math.Max(1,h/2);
            float[] expected=new float[nextW*nextH];
            // A parent's normalized interval overlaps these source intervals. This reference uses
            // interval intersection rather than the shader's integer footprint implementation.
            for(int y=0;y<nextH;y++)for(int x=0;x<nextW;x++) {
                float minimum=1;
                for(int sy=Math.Max(0,(int)((long)y*h/nextH));sy<Math.Min(h,(int)(((long)(y+1)*h+nextH-1)/nextH));sy++)
                for(int sx=Math.Max(0,(int)((long)x*w/nextW));sx<Math.Min(w,(int)(((long)(x+1)*w+nextW-1)/nextW));sx++)
                    if((long)sx*nextW<(long)(x+1)*w && (long)(sx+1)*nextW>(long)x*w &&
                       (long)sy*nextH<(long)(y+1)*h && (long)(sy+1)*nextH>(long)y*h)
                        minimum=Math.Min(minimum,values[sy*w+sx]);
                expected[y*nextW+x]=minimum;
            }
            values=expected;w=nextW;h=nextH;
        }
        Assert.Equal(.001f,values[0]);
#if DEBUG
        Assert.Equal(bytes,owner.StorageBytes);
#endif
        // GPU comparisons cover every texel of the original mip chain without downloading its images.
        Assert.Same(texture,owner.Texture);
        owner.Prepare(width,height,compute);Assert.Same(texture,owner.Texture);
        owner.Prepare(width+2,height+2,compute);Assert.False(texture.IsValid);
        owner.Dispose();Assert.Null(owner.Texture);
#if DEBUG
        Assert.Equal(0,owner.StorageBytes);
#endif
        Assert.Equal(ErrorCode.NoError,GL.GetError());
    }
    /// <summary>The last-group reset allows consecutive changed frames and a resized allocation to publish complete tails.</summary>
    [Fact]
    public void RepeatedFramesReuseCounterAndResizeStorage()
    {
        // Arrange: Exercise odd, tall, and wide extents with a reusable production owner and compute program.
        EnsureShaderTestAvailable();
        using var owner=new DepthHierarchyPass();
        var compute=Programs.CreateDepthHierarchy();
        // Act and assert: Change every source each frame, verify every mip, then resize and dispose between extents.
        foreach(var extent in new[] { (257,131), (321,241), (1,513), (513,1) }) {
            int width=extent.Item1,height=extent.Item2;
            owner.Prepare(width,height,compute);
            var texture=owner.Texture!;
            using var source=TestFramework.CreateTexture(width,height,TextureFormat.R32f);
            for(int frame=0;frame<8;frame++) {
                float depth=.125f+frame*.0625f;
                source.UploadDataImmediate(Enumerable.Repeat(depth,width*height).ToArray());
                owner.Render(source);
                for(int level=0;level<texture.MipLevels;level++)
                    ImageAssertions.Comparisons.CompareConstant(texture, depth, Assertions.GpuImageSnapshot.Mip(texture, level)).AssertEqual();
                owner.Prepare(width,height,compute);
                Assert.Same(texture,owner.Texture);
            }
        }
        owner.Dispose(); Assert.Null(owner.Texture); Assert.Equal(0,owner.ScratchBytes);
        Assert.Equal(ErrorCode.NoError,GL.GetError());
    }

    /// <summary>Prepares this suite's declared observation programs before subject execution.</summary>
    #endregion

    #region Protected API
    private protected override void PrepareObservations(VanillaGraphicsExpanded.Tests.Support.GPU.Observations.GpuObservationPrograms programs)
    {
        base.PrepareObservations(programs);
        programs.Prepare("compare", 0, 0);
    }
    #endregion
}
