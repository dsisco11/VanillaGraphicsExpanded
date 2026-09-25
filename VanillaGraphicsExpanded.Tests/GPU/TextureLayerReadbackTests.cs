using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks layered region readback and the caller's independent framebuffer bindings.</summary>
[Collection("GPU")]
[Trait("Category","GPU")]
public sealed class TextureLayerReadbackTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Layer readback
    /// <summary>Distinct per-coordinate data verifies layer selection, XY offsets, channel ordering and binding restoration.</summary>
    [Theory]
    [InlineData(TextureTarget.Texture3D)]
    [InlineData(TextureTarget.Texture2DArray)]
    public void RegionSelectsLayerAndPreservesFramebufferBindings(TextureTarget target)
    {
        EnsureContextValid();
        using var texture=Texture3D.Create(5,4,3,PixelInternalFormat.Rgba32f,textureTarget:target);
        var data=new float[5*4*3*4];
        for(int z=0;z<3;z++) for(int y=0;y<4;y++) for(int x=0;x<5;x++)
        {
            int i=(((z*4+y)*5+x)<<2);
            data[i]=x;data[i+1]=y;data[i+2]=z;data[i+3]=100+x+10*y+100*z;
        }
        texture.UploadDataImmediate(data,0,0,0,5,4,3);
        using var draw=GpuFramebuffer.CreateEmpty("Tests.Readback.Draw");
        using var read=GpuFramebuffer.CreateEmpty("Tests.Readback.Read");
        using var drawScope=GlStateCache.Current.BindFramebufferScope(FramebufferTarget.DrawFramebuffer,draw.FboId);
        using var readScope=GlStateCache.Current.BindFramebufferScope(FramebufferTarget.ReadFramebuffer,read.FboId);
        using var pack=GpuPixelPackBuffer.Create(debugName:"Tests.Readback.ExistingPack");
        using var packScope=pack.BindScope();
        var packing=new[]{PixelStoreParameter.PackAlignment,PixelStoreParameter.PackRowLength,
            PixelStoreParameter.PackSkipRows,PixelStoreParameter.PackSkipPixels,PixelStoreParameter.PackSwapBytes};
        var previous=packing.Select(value=>GL.GetInteger((GetPName)value)).ToArray();
        int[] hostile=[8,17,3,5,1];

        try
        {
            for(int i=0;i<packing.Length;i++) GL.PixelStore(packing[i],hostile[i]);
            GlStateCache.Current.DirtyPixelPackState();
            using var result=texture.ReadPixelsRegion(2,1,2,2,2);
            Assert.True(result.Span.SequenceEqual(new float[]{2,1,2,312,3,1,2,313,2,2,2,322,3,2,2,323}));
            for(int i=0;i<packing.Length;i++) Assert.Equal(hostile[i],GL.GetInteger((GetPName)packing[i]));
            Assert.Equal(pack.BufferId,GL.GetInteger(GetPName.PixelPackBufferBinding));
        }
        finally
        {
            for(int i=0;i<packing.Length;i++) GL.PixelStore(packing[i],previous[i]);
            GlStateCache.Current.DirtyPixelPackState();
        }

        Assert.Equal(draw.FboId,GL.GetInteger(GetPName.DrawFramebufferBinding));
        Assert.Equal(read.FboId,GL.GetInteger(GetPName.ReadFramebufferBinding));
        Assert.Throws<ArgumentOutOfRangeException>(()=>texture.ReadPixelsRegion(0,0,1,1,3));
        Assert.Throws<ArgumentOutOfRangeException>(()=>texture.ReadPixelsRegion(4,1,2,1,0));
        Assert.Throws<ArgumentOutOfRangeException>(()=>texture.ReadPixelsRegion(0,0,0,1,0));
        Assert.Equal(draw.FboId,GL.GetInteger(GetPName.DrawFramebufferBinding));
        Assert.Equal(read.FboId,GL.GetInteger(GetPName.ReadFramebufferBinding));
        Assert.Equal(ErrorCode.NoError,GL.GetError());
    }
    #endregion
}
