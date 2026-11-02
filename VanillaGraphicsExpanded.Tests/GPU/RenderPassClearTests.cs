using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Pipeline.Passes;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks pass loads against actual image contents and hostile incoming drawing state.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
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
            });
            using var signedContents = Assertions.GpuImageSnapshot.FromAttachment(signed);
            using var unsignedContents = Assertions.GpuImageSnapshot.FromAttachment(unsigned);
            using var floatContents = Assertions.GpuImageSnapshot.FromAttachment(floats);
            ImageAssertions.Comparisons.CompareIntegerVector(signedContents.Texture,
                unchecked((uint)-3),7,unchecked((uint)-16777217),19).AssertEqual();
            ImageAssertions.Comparisons.CompareIntegerVector(unsignedContents.Texture,3,7,4294967294,19).AssertEqual();
            ImageAssertions.Comparisons.CompareVector(floatContents.Texture,new(.25f,.5f,.75f,1)).AssertEqual();
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
            ImageAssertions.Statistics.Inspect(texture, clearDepth ? .749f : .249f, clearDepth ? .751f : .251f).AssertFiniteRange();
            ImageAssertions.Comparisons.CompareStencil(texture,clearDepth ? (byte)0x36 : (byte)0x59).AssertEqual();
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
        ImageAssertions.Comparisons.CompareConstant(target[1], .75f).AssertEqual();
        foreach (var border in new RHITextureRegion[] {
            new(new(0),0,0,0,4,1), new(new(0),0,3,0,4,1),
            new(new(0),0,1,0,1,2), new(new(0),3,1,0,1,2) })
            ImageAssertions.Comparisons.CompareConstant(target[0], .25f, border).AssertEqual();
        if (!discard) ImageAssertions.Comparisons.CompareConstant(target[0], 1, new(new(0),1,1,0,2,2)).AssertEqual();
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
        if (discardDepth) ImageAssertions.Comparisons.CompareStencil(texture,0x47).AssertEqual();
        else ImageAssertions.Statistics.Inspect(texture, .624f, .626f).AssertFiniteRange();
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }
    #endregion
    #region Private
    /// <summary>Prepares this suite's declared observation programs before subject execution.</summary>
    private protected override void PrepareObservations(VanillaGraphicsExpanded.Tests.GPU.Assertions.GpuObservationPrograms programs)
    {
        base.PrepareObservations(programs);
        programs.Prepare("compare", 2, 0);
        programs.Prepare("compare", 1, 0);
        programs.Prepare("compare", 0, 0);
        programs.Prepare("inspection", 0, 0);
    }
    #endregion
}
