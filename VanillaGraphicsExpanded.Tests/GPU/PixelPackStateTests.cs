using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using PackState = VanillaGraphicsExpanded.Rendering.GlStateCache.PixelPackState;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks cached pack layouts against independently observed driver state.</summary>
[Collection("GPU")]
[Trait("Category","GPU")]
public sealed class PixelPackStateTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region State transitions
    /// <summary>Nested scopes restore all layout fields, including when an inner operation throws.</summary>
    [Fact]
    public void NestedScopesRestoreDriverAndCache()
    {
        EnsureContextValid();
        var cache=GlStateCache.Current;
        using var restore=cache.SetPixelPackScope(new(8,19,3,7,true));
        var outer=cache.GetPixelPackState();
        using(cache.SetPixelPackScope(new(1,11,2,4)))
        {
            AssertState(new(1,11,2,4));
            Assert.Throws<InvalidOperationException>((Action)(()=>
            {
                using var inner=cache.SetPixelPackScope(new(2,8,1,2,true));
                AssertState(new(2,8,1,2,true));
                throw new InvalidOperationException("controlled operation failure");
            }));
            AssertState(new(1,11,2,4));
        }
        AssertState(outer);
    }

    /// <summary>Targeted and complete invalidation discover raw external changes before restoring borrowed layouts.</summary>
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public void InvalidationDiscoversExternalState(int boundary)
    {
        EnsureContextValid();
        var cache=GlStateCache.Current;
        using var restore=cache.SetPixelPackScope(new(4));
        GL.PixelStore(PixelStoreParameter.PackAlignment,8);
        GL.PixelStore(PixelStoreParameter.PackRowLength,23);
        GL.PixelStore(PixelStoreParameter.PackSkipRows,2);
        GL.PixelStore(PixelStoreParameter.PackSkipPixels,5);
        GL.PixelStore(PixelStoreParameter.PackSwapBytes,1);
        switch(boundary)
        {
            case 0: cache.DirtyPixelPackState(); break;
            case 1: cache.InvalidateAll(); break;
            case 2: cache.PurgeCache(); break;
            default: cache.BeginFrame(); break;
        }
        var external=new PackState(8,23,2,5,true);
        AssertState(external);
        using(cache.SetPixelPackScope(new(4))) AssertState(new(4));
        AssertState(external);
    }

    /// <summary>Rejected values neither change driver state nor poison the cached layout.</summary>
    [Theory]
    [InlineData(3,0,0,0)] [InlineData(4,-1,0,0)]
    [InlineData(4,0,-1,0)] [InlineData(4,0,0,-1)]
    public void InvalidLayoutsDoNotMutateState(int alignment,int rowLength,int rows,int pixels)
    {
        EnsureContextValid();
        var cache=GlStateCache.Current;
        using var restore=cache.SetPixelPackScope(new(8,9,1,3,true));
        Assert.Throws<ArgumentOutOfRangeException>(()=>cache.SetPixelPackState(new(alignment,rowLength,rows,pixels)));
        AssertState(new(8,9,1,3,true));
    }
    #endregion

    #region Driver observations
    /// <summary>Compares every tracked value to actual OpenGL state rather than cache values alone.</summary>
    private static void AssertState(PackState expected)
    {
        Assert.Equal(expected,GlStateCache.Current.GetPixelPackState());
        Assert.Equal(expected.Alignment,GL.GetInteger(GetPName.PackAlignment));
        Assert.Equal(expected.RowLength,GL.GetInteger(GetPName.PackRowLength));
        Assert.Equal(expected.SkipRows,GL.GetInteger(GetPName.PackSkipRows));
        Assert.Equal(expected.SkipPixels,GL.GetInteger(GetPName.PackSkipPixels));
        Assert.Equal(expected.SwapBytes,GL.GetInteger(GetPName.PackSwapBytes)!=0);
        Assert.Equal(ErrorCode.NoError,GL.GetError());
    }
    #endregion
}
