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
        using var target = GpuFramebuffer.CreateEmpty();
        using var floatStorage = DynamicTexture2D.Create(4, 4, PixelInternalFormat.Rgba32f);
        using GpuResource signedStorage = textureStorage ? DynamicTexture2D.Create(4, 4, PixelInternalFormat.Rgba32i)
            : GpuRenderbuffer.Create(RenderbufferStorage.Rgba32i, 4, 4);
        using GpuResource unsignedStorage = textureStorage ? DynamicTexture2D.Create(4, 4, PixelInternalFormat.Rgba32ui)
            : GpuRenderbuffer.Create(RenderbufferStorage.Rgba32ui, 4, 4);
        using var floats = GpuFramebufferAttachment.FromTexture(floatStorage);
        using var signed = signedStorage is GpuTexture signedTexture ? GpuFramebufferAttachment.FromTexture(signedTexture)
            : GpuFramebufferAttachment.FromRenderbuffer((GpuRenderbuffer)signedStorage);
        using var unsigned = unsignedStorage is GpuTexture unsignedTexture ? GpuFramebufferAttachment.FromTexture(unsignedTexture)
            : GpuFramebufferAttachment.FromRenderbuffer((GpuRenderbuffer)unsignedStorage);
        target.SetAttachment(FramebufferAttachment.ColorAttachment0, floats);
        target.SetAttachment(FramebufferAttachment.ColorAttachment2, signed);
        target.SetAttachment(FramebufferAttachment.ColorAttachment4, unsigned);
        GL.ColorMask(false, false, false, false); GL.Enable(EnableCap.ScissorTest); GL.Scissor(0, 0, 0, 0);
        StateCache.Current.InvalidateAll();
        try
        {
            RenderPassTestBoundary.Run(boundary =>
            {
                using var pass = RenderPass.Begin(new(target,
                    [new(2, AttachmentLoad.Clear, Clear: ColorClearValue.Int(-3, 7, -16777217, 19)),
                     new(-1, DiscardOutput: true),
                     new(0, AttachmentLoad.Clear, Clear: ColorClearValue.Float(.25f, .5f, .75f, 1)),
                     new(4, AttachmentLoad.Clear, Clear: ColorClearValue.UInt(3, 7, 4294967294, 19))]), boundary);
                pass.Validate();
                using var pack = StateCache.Current.SetPixelPackScope(new(1));
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
        finally { GL.ColorMask(true, true, true, true); GL.Disable(EnableCap.ScissorTest); StateCache.Current.InvalidateAll(); }
    }

    /// <summary>A depth or stencil load changes only its requested packed aspect despite disabled write masks.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void PackedDepthStencilPreservesUnrequestedAspect(bool clearDepth)
    {
        EnsureContextValid();
        using var texture = new DepthStencilTexture(4, 4);
        using var target = GpuFramebuffer.CreateDepthOnly(texture)!;
        using var bindings = StateCache.Current.BindFramebufferScope();
        target.Bind(); GL.Disable(EnableCap.ScissorTest); GL.DepthMask(true); GL.StencilMask(0xff);
        GL.ClearDepth(.25); GL.ClearStencil(0x36); GL.Clear(ClearBufferMask.DepthBufferBit | ClearBufferMask.StencilBufferBit);
        GL.DepthMask(false); GL.StencilMask(0); StateCache.Current.InvalidateAll();
        try
        {
            RenderPassTestBoundary.Run(boundary =>
            {
                using var pass = RenderPass.Begin(new(target, [], new(
                    DepthLoad: clearDepth ? AttachmentLoad.Clear : AttachmentLoad.Preserve, ClearDepth: .75f,
                    StencilLoad: clearDepth ? AttachmentLoad.Preserve : AttachmentLoad.Clear, ClearStencil: 0x59)), boundary);
            });
            Assert.All(texture.ReadPixels(), value => Assert.InRange(value, clearDepth ? .749f : .249f, clearDepth ? .751f : .251f));
            Assert.All(texture.ReadStencilPixels(), value => Assert.Equal(clearDepth ? (byte)0x36 : (byte)0x59, value));
            Assert.False(GL.GetBoolean(GetPName.DepthWritemask));
            Assert.Equal(0, GL.GetInteger(GetPName.StencilWritemask));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { GL.DepthMask(true); GL.StencilMask(0xff); StateCache.Current.InvalidateAll(); }
    }

    /// <summary>Clears are restricted to the declared area; preserve and discard leave other attachments intact.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AreaAndUnrequestedColorContentsArePreserved(bool discard)
    {
        EnsureContextValid();
        using var target = CreateMRTRenderTarget(4, 4, PixelInternalFormat.Rgba32f, PixelInternalFormat.Rgba32f);
        target[0].UploadDataImmediate(Enumerable.Repeat(.25f, 64).ToArray());
        target[1].UploadDataImmediate(Enumerable.Repeat(.75f, 64).ToArray());
        RenderPassTestBoundary.Run(boundary =>
        {
            using var pass = RenderPass.Begin(new(target,
                [new(0, discard ? AttachmentLoad.Discard : AttachmentLoad.Clear,
                    discard ? AttachmentStore.Discard : AttachmentStore.Preserve,
                    discard ? null : ColorClearValue.Float(1, 1, 1, 1)), new(1)], area: new(1, 1, 2, 2)), boundary);
            Assert.Equal(2, pass.Area.Width); Assert.Equal(2, pass.Area.Height);
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
        using var target = GpuFramebuffer.CreateDepthOnly(texture)!;
        using var bindings = StateCache.Current.BindFramebufferScope();
        target.Bind(); GL.Disable(EnableCap.ScissorTest); GL.DepthMask(true); GL.StencilMask(0xff);
        GL.ClearDepth(.625); GL.ClearStencil(0x47); GL.Clear(ClearBufferMask.DepthBufferBit | ClearBufferMask.StencilBufferBit);
        StateCache.Current.InvalidateAll();
        RenderPassTestBoundary.Run(boundary =>
        {
            using var pass = RenderPass.Begin(new(target, [], new(
                DepthLoad: discardDepth ? AttachmentLoad.Discard : AttachmentLoad.Preserve,
                DepthStore: discardDepth ? AttachmentStore.Discard : AttachmentStore.Preserve,
                StencilLoad: discardDepth ? AttachmentLoad.Preserve : AttachmentLoad.Discard,
                StencilStore: discardDepth ? AttachmentStore.Preserve : AttachmentStore.Discard)), boundary);
        });
        // Only the retained aspect has a defined value under a discard contract.
        if (discardDepth) Assert.All(texture.ReadStencilPixels(), value => Assert.Equal((byte)0x47, value));
        else Assert.All(texture.ReadPixels(), value => Assert.InRange(value, .624f, .626f));
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }
    #endregion
}
