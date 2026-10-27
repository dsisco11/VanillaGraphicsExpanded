using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Pipeline.Passes;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks pass loads against actual image contents and hostile incoming drawing state.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class RenderPassClearTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Public API
    /// <summary>Sparse output routes clear the intended attachment with its numeric type and leave holes untouched.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SparseRoutesUseTypedAttachmentClears(bool textureStorage)
    {
        EnsureContextValid();
        using var target = DynamicGpuFramebuffer.CreateEmpty();
        using var floatStorage = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba32f, Width = System.Math.Max(1, 4), Height = System.Math.Max(1, 4), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        using GpuResource signedStorage = textureStorage ? RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba32i, Width = System.Math.Max(1, 4), Height = System.Math.Max(1, 4), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource()
            : RHICommandContext.Current.CreateRenderbuffer(new RHIRenderbufferDescription { Format = TextureFormat.Rgba32i, Width = 4, Height = 4, Samples = 0, Label = null }).RequireResource();
        using GpuResource unsignedStorage = textureStorage ? RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba32ui, Width = System.Math.Max(1, 4), Height = System.Math.Max(1, 4), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource()
            : RHICommandContext.Current.CreateRenderbuffer(new RHIRenderbufferDescription { Format = TextureFormat.Rgba32ui, Width = 4, Height = 4, Samples = 0, Label = null }).RequireResource();
        using var floats = GpuFramebufferAttachment.FromTexture(floatStorage);
        using var signed = signedStorage is GpuTexture signedTexture ? GpuFramebufferAttachment.FromTexture(signedTexture)
            : GpuFramebufferAttachment.FromRenderbuffer((GpuRenderbuffer)signedStorage);
        using var unsigned = unsignedStorage is GpuTexture unsignedTexture ? GpuFramebufferAttachment.FromTexture(unsignedTexture)
            : GpuFramebufferAttachment.FromRenderbuffer((GpuRenderbuffer)unsignedStorage);
        target.SetAttachment(FramebufferSlot.Color0, floats);
        target.SetAttachment(FramebufferSlot.Color2, signed);
        target.SetAttachment(FramebufferSlot.Color4, unsigned);
        GL.ColorMask(false, false, false, false); GL.Enable(EnableCap.ScissorTest); GL.Scissor(0, 0, 0, 0);
        OpenGlStateCache.Current.InvalidateAll();
        try
        {
            RenderPassTestBoundary.Run(boundary =>
            {
                boundary.BeginRenderPass(FramebufferTestSetup.DescribeAttachments(target,
                    [new(AttachmentIndex:2, Load:AttachmentLoad.Clear, Clear: ColorClearValue.Int(-3, 7, -16777217, 19)),
                     new(DiscardOutput: true),
                     new(AttachmentIndex:0, Load:AttachmentLoad.Clear, Clear: ColorClearValue.Float(.25f, .5f, .75f, 1)),
                     new(AttachmentIndex:4, Load:AttachmentLoad.Clear, Clear: ColorClearValue.UInt(3, 7, 4294967294, 19))]));
                Assert.True(boundary.HasRenderPass);
                using var reads = OpenGlStateCache.Current.BindFramebufferScope();
                OpenGLFramebufferSelection.Bind(OpenGlStateCache.Current, FramebufferTarget.ReadFramebuffer, target.FboId);
                using var pack = OpenGLPixelLayouts.PackScope(OpenGlStateCache.Current, new(1));
                GL.ReadBuffer(ReadBufferMode.ColorAttachment2);
                int[] signedPixels = new int[64]; GL.ReadPixels(0, 0, 4, 4, PixelFormat.RgbaInteger, PixelType.Int, signedPixels);
                GL.ReadBuffer(ReadBufferMode.ColorAttachment4);
                uint[] unsignedPixels = new uint[64]; GL.ReadPixels(0, 0, 4, 4, PixelFormat.RgbaInteger, PixelType.UnsignedInt, unsignedPixels);
                GL.ReadBuffer(ReadBufferMode.ColorAttachment0);
                float[] floatPixels = new float[64]; GL.ReadPixels(0, 0, 4, 4, PixelFormat.Rgba, PixelType.Float, floatPixels);
                for (int i = 0; i < 64; i++)
                {
                    Assert.Equal(new[] { -3, 7, -16777217, 19 }[i % 4], signedPixels[i]);
                    Assert.Equal(new uint[] { 3, 7, 4294967294, 19 }[i % 4], unsignedPixels[i]);
                    Assert.Equal(new[] { .25f, .5f, .75f, 1 }[i % 4], floatPixels[i]);
                }
            });
            Assert.True(GL.IsEnabled(EnableCap.ScissorTest));
            bool[] mask = new bool[4]; GL.GetBoolean(GetPName.ColorWritemask, mask); Assert.All(mask, Assert.False);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { GL.ColorMask(true, true, true, true); GL.Disable(EnableCap.ScissorTest); OpenGlStateCache.Current.InvalidateAll(); }
    }

    /// <summary>A depth or stencil load changes only its requested packed aspect despite disabled write masks.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void PackedDepthStencilPreservesUnrequestedAspect(bool clearDepth)
    {
        EnsureContextValid();
        using var texture = new DepthStencilTexture(4, 4);
        using var target = DynamicGpuFramebuffer.CreateDepthOnly(texture)!;
        using var bindings = OpenGlStateCache.Current.BindFramebufferScope();
        target.Bind(); GL.Disable(EnableCap.ScissorTest); GL.DepthMask(true); GL.StencilMask(0xff);
        GL.ClearDepth(.25); GL.ClearStencil(0x36); GL.Clear(ClearBufferMask.DepthBufferBit | ClearBufferMask.StencilBufferBit);
        GL.DepthMask(false); GL.StencilMask(0); OpenGlStateCache.Current.InvalidateAll();
        try
        {
            RenderPassTestBoundary.Run(boundary =>
            {
                boundary.BeginRenderPass(FramebufferTestSetup.DescribeAttachments(target, [], new(
                    DepthLoad: clearDepth ? AttachmentLoad.Clear : AttachmentLoad.Preserve, ClearDepth: .75f,
                    StencilLoad: clearDepth ? AttachmentLoad.Preserve : AttachmentLoad.Clear, ClearStencil: 0x59)));
            });
            Assert.All(texture.ReadPixels(), value => Assert.InRange(value, clearDepth ? .749f : .249f, clearDepth ? .751f : .251f));
            Assert.All(texture.ReadStencilPixels(), value => Assert.Equal(clearDepth ? (byte)0x36 : (byte)0x59, value));
            Assert.False(GL.GetBoolean(GetPName.DepthWritemask));
            Assert.Equal(0, GL.GetInteger(GetPName.StencilWritemask));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { GL.DepthMask(true); GL.StencilMask(0xff); OpenGlStateCache.Current.InvalidateAll(); }
    }

    /// <summary>Clears are restricted to the declared area; preserve and discard leave other attachments intact.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AreaAndUnrequestedColorContentsArePreserved(bool discard)
    {
        EnsureContextValid();
        using var target = CreateMRTRenderTarget(4, 4, TextureFormat.Rgba32f, TextureFormat.Rgba32f);
        target[0].UploadDataImmediate(Enumerable.Repeat(.25f, 64).ToArray());
        target[1].UploadDataImmediate(Enumerable.Repeat(.75f, 64).ToArray());
        RenderPassTestBoundary.Run(boundary =>
        {
            boundary.BeginRenderPass(FramebufferTestSetup.DescribeAttachments(target,
                [new(AttachmentIndex:0, Load:discard ? AttachmentLoad.Discard : AttachmentLoad.Clear,
                    Store:discard ? AttachmentStore.Discard : AttachmentStore.Preserve,
                    Clear:discard ? null : ColorClearValue.Float(1, 1, 1, 1)), new(AttachmentIndex:1)], area: new(1, 1, 2, 2)));
            Assert.Equal(2, boundary.PassViewport.Width); Assert.Equal(2, boundary.PassViewport.Height);
        });
        Assert.All(target[1].ReadPixels(), value => Assert.Equal(.75f, value));
        float[] pixels = target[0].ReadPixels();
        for (int y = 0; y < 4; y++) for (int x = 0; x < 4; x++)
        {
            bool inside = x is 1 or 2 && y is 1 or 2;
            if (discard && inside) continue; // Discard deliberately provides no content guarantee in this region.
            for (int component = 0; component < 4; component++) Assert.Equal(inside ? 1 : .25f, pixels[(y * 4 + x) * 4 + component]);
        }
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>Discarding either packed aspect cannot abandon the independently preserved aspect.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PackedAspectDiscardPreservesOtherAspect(bool discardDepth)
    {
        EnsureContextValid();
        using var texture = new DepthStencilTexture(4, 4);
        using var target = DynamicGpuFramebuffer.CreateDepthOnly(texture)!;
        using var bindings = OpenGlStateCache.Current.BindFramebufferScope();
        target.Bind(); GL.Disable(EnableCap.ScissorTest); GL.DepthMask(true); GL.StencilMask(0xff);
        GL.ClearDepth(.625); GL.ClearStencil(0x47); GL.Clear(ClearBufferMask.DepthBufferBit | ClearBufferMask.StencilBufferBit);
        OpenGlStateCache.Current.InvalidateAll();
        RenderPassTestBoundary.Run(boundary =>
        {
            boundary.BeginRenderPass(FramebufferTestSetup.DescribeAttachments(target, [], new(
                DepthLoad: discardDepth ? AttachmentLoad.Discard : AttachmentLoad.Preserve,
                DepthStore: discardDepth ? AttachmentStore.Discard : AttachmentStore.Preserve,
                StencilLoad: discardDepth ? AttachmentLoad.Preserve : AttachmentLoad.Discard,
                StencilStore: discardDepth ? AttachmentStore.Preserve : AttachmentStore.Discard)));
        });
        // Only the retained aspect has a defined value under a discard contract.
        if (discardDepth) Assert.All(texture.ReadStencilPixels(), value => Assert.Equal((byte)0x47, value));
        else Assert.All(texture.ReadPixels(), value => Assert.InRange(value, .624f, .626f));
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }
    #endregion
}
