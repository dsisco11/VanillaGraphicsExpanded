using System.Runtime.CompilerServices;
using HarmonyLib;
using VanillaGraphicsExpanded.PBR.Liquids;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using Vintagestory.API.Client;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks retained liquid preparation against engine membership and published target changes.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class LiquidPreparationTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Public API
    /// <summary>A real native configuration mutation expires and replaces the receipt before admission.</summary>
    [Fact]
    public void VertexConfigurationRefreshDoesNotRequireMembershipChange()
    {
        EnsureContextValid();
        using var storage = new LiquidPoolStorage();
        using var prepared = new LiquidGeometryPreparation();
        var manager = Manager([storage.Pool]);
        prepared.Prepare([manager]);
        var original = prepared[storage.Pool];
        OpenTK.Graphics.OpenGL4.GL.VertexArrayVertexBuffer(storage.Mesh.VaoId, 0, storage.Mesh.xyzVboId, IntPtr.Zero, 12);
        Assert.False(original.IsPrepared);
        prepared.Prepare([manager]);
        Assert.NotSame(original, prepared[storage.Pool]);
        Assert.True(prepared[storage.Pool].IsPrepared);
    }

    /// <summary>Canonical buffer changes invalidate retained geometry without requiring a membership change.</summary>
    [Fact]
    public void ResourcePublicationInvalidatesPreparation()
    {
        EnsureContextValid();
        using var storage = new LiquidPoolStorage();
        using var prepared = new LiquidGeometryPreparation();
        var manager = Manager([storage.Pool]);
        prepared.Prepare([manager]);
        var original = prepared[storage.Pool];
        // Make the native stream too small. Its existing recipient receives the committed facts;
        // the next pre-boundary preparation must reject it rather than silently reuse an old receipt.
        OpenTK.Graphics.OpenGL4.GL.NamedBufferData(storage.Mesh.customDataIntVboId, 4, IntPtr.Zero,
            OpenTK.Graphics.OpenGL4.BufferUsageHint.StaticDraw);
        Assert.ThrowsAny<Exception>(() => prepared.Prepare([manager]));
        Assert.Same(original, prepared[storage.Pool]);
        OpenTK.Graphics.OpenGL4.GL.NamedBufferData(storage.Mesh.customDataIntVboId, 24, IntPtr.Zero,
            OpenTK.Graphics.OpenGL4.BufferUsageHint.StaticDraw);
        prepared.Prepare([manager]);
        Assert.True(prepared[storage.Pool].IsPrepared);
    }

    /// <summary>The installed disposal hook expires borrowers before invoking engine mesh deletion.</summary>
    [Fact]
    public void EngineDisposalRetiresGeometryBeforeDeleteMesh()
    {
        EnsureContextValid();
        using var storage = new LiquidPoolStorage();
        using var prepared = new LiquidGeometryPreparation();
        prepared.Prepare([Manager([storage.Pool])]);
        var original = prepared[storage.Pool];
        var render = new Moq.Mock<IRenderAPI>();
        var api = new Moq.Mock<ICoreClientAPI>();
        api.SetupGet(a => a.Render).Returns(render.Object);
        render.Setup(r => r.DeleteMesh(storage.Mesh)).Callback(() => Assert.False(original.IsPrepared));
        var harmony = new Harmony("VGE.Tests.LiquidGeometryRetirement");
        try
        {
            harmony.CreateClassProcessor(typeof(VanillaGraphicsExpanded.HarmonyPatches.LiquidGeometryPoolRetirementHook)).Patch();
            storage.Pool.Dispose(api.Object);
            Assert.Throws<InvalidOperationException>(() => prepared[storage.Pool]);
            render.Verify(r => r.DeleteMesh(storage.Mesh), Moq.Times.Once);
        }
        finally { harmony.UnpatchAll(harmony.Id); }
    }

    /// <summary>Stable membership reuses adapters; growth, failed candidates, removal and retirement preserve ownership.</summary>
    [Fact]
    public void MembershipRefreshIsTransactionalAndRetainsUnchangedGeometry()
    {
        EnsureContextValid();
        using var first = new LiquidPoolStorage();
        using var second = new LiquidPoolStorage();
        using var prepared = new LiquidGeometryPreparation();
        var pools = new List<MeshDataPool> { first.Pool };
        var manager = Manager(pools);
        prepared.Prepare([manager]);
        var original = prepared[first.Pool];
        prepared.Prepare([manager]);
        Assert.Same(original, prepared[first.Pool]);

        pools.Add(second.Pool);
        VanillaGraphicsExpanded.HarmonyPatches.LiquidGeometryPoolAdditionHook.Finalizer(pools, 1);
        int stream = second.Mesh.customDataIntVboId;
        second.Mesh.customDataIntVboId = 0;
        Assert.Throws<InvalidOperationException>(() => prepared.Prepare([manager]));
        Assert.Same(original, prepared[first.Pool]);
        Assert.True(original.IsPrepared);
        second.Mesh.customDataIntVboId = stream;
        prepared.Prepare([manager]);
        Assert.Same(original, prepared[first.Pool]);
        var added = prepared[second.Pool];

        pools.Remove(first.Pool);
        LiquidGeometryLifecycle.Retire(first.Pool);
        prepared.Prepare([manager]);
        Assert.False(original.IsPrepared);
        Assert.Same(added, prepared[second.Pool]);
        Assert.Throws<InvalidOperationException>(() => prepared[first.Pool]);
        // A replacement manager with the same count must not reuse the previous membership.
        LiquidGeometryLifecycle.ChangedCollections();
        prepared.Prepare([Manager([first.Pool])]);
        Assert.False(added.IsPrepared);
        Assert.NotSame(original, prepared[first.Pool]);
        prepared.Dispose();
        Assert.False(first.Mesh.Disposed);
        Assert.False(second.Mesh.Disposed);
    }

    /// <summary>Changed geometry is rebuilt outside submission, while invalid replacements leave the old adapter installed.</summary>
    [Fact]
    public void GeometryRefreshPreservesCandidateFailureAndDynamicIntervals()
    {
        EnsureContextValid();
        using var first = new LiquidPoolStorage();
        using var second = new LiquidPoolStorage();
        using var prepared = new LiquidGeometryPreparation();
        var manager = Manager([first.Pool]);
        prepared.Prepare([manager]);
        var original = prepared[first.Pool];
        first.Pool.indicesSizes[0] = 6;
        Assert.Throws<InvalidOperationException>(() => original.CaptureDraws().ToArray());
        first.Pool.indicesSizes[0] = 3;
        OpenTK.Graphics.OpenGL4.GL.NamedBufferData(first.Mesh.customDataIntVboId, 4, IntPtr.Zero,
            OpenTK.Graphics.OpenGL4.BufferUsageHint.StaticDraw);
        Assert.False(original.IsPrepared);
        Assert.Throws<ArgumentOutOfRangeException>(() => prepared.Prepare([manager]));
        Assert.Same(original, prepared[first.Pool]);
        Assert.Throws<InvalidOperationException>(() => original.CaptureDraws().ToArray());
        // The failed refresh stays withdrawn. A legal owner replacement supplies complete storage;
        // the already queued affected pool is retried only at the next explicit preparation boundary.
        AccessTools.Field(typeof(MeshDataPool), "modelRef").SetValue(first.Pool, second.Mesh);
        prepared.Prepare([manager]);
        Assert.NotSame(original, prepared[first.Pool]);
        Assert.False(original.IsPrepared);
        Assert.True(prepared[first.Pool].IsPrepared);
    }

    /// <summary>Source-only refresh visits its affected adapter without rediscovering engine membership or replacing peers.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DeliveredWithdrawalRefreshesOnlyItsPool(bool storageChange)
    {
        EnsureContextValid();
        using var first = new LiquidPoolStorage();
        using var second = new LiquidPoolStorage();
        using var prepared = new LiquidGeometryPreparation();
        var manager = Manager([first.Pool, second.Pool]);
        prepared.Prepare([manager]);
        var original = prepared[first.Pool];
        var peer = prepared[second.Pool];
        // Unchanged preparation consumes neither the manager row nor a retained-adapter identity scan.
        prepared.Prepare([]);
        Assert.Same(original, prepared[first.Pool]);
        if (storageChange)
            OpenTK.Graphics.OpenGL4.GL.NamedBufferData(first.Mesh.customDataIntVboId, 24, IntPtr.Zero,
                OpenTK.Graphics.OpenGL4.BufferUsageHint.StaticDraw);
        else
            OpenTK.Graphics.OpenGL4.GL.VertexArrayVertexBuffer(first.Mesh.VaoId, 0, first.Mesh.xyzVboId, IntPtr.Zero, 12);
        Assert.False(original.IsPrepared);
        Assert.True(peer.IsPrepared);
        Assert.Throws<InvalidOperationException>(() => original.CaptureDraws().ToArray());
        // No topology notification occurred, so the supplied row is unused: only queued pools refresh.
        prepared.Prepare([]);
        Assert.NotSame(original, prepared[first.Pool]);
        Assert.Same(peer, prepared[second.Pool]);
        Assert.True(prepared[first.Pool].IsPrepared);
        Assert.Equal(3, prepared[first.Pool].CaptureDraws()[0].IndexCount);
        prepared.Prepare([]);
        Assert.Same(peer, prepared[second.Pool]);
    }

    /// <summary>Observed native deletion withdraws the original borrow before an owner supplies replacement storage.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NativeRetirementCannotRetargetOriginalAdapter(bool deleteArray)
    {
        EnsureContextValid();
        using var first = new LiquidPoolStorage();
        using var replacement = new LiquidPoolStorage();
        using var prepared = new LiquidGeometryPreparation();
        prepared.Prepare([Manager([first.Pool])]);
        var original = prepared[first.Pool];
        if (deleteArray) OpenTK.Graphics.OpenGL4.GL.DeleteVertexArray(first.Mesh.VaoId);
        else OpenTK.Graphics.OpenGL4.GL.DeleteBuffer(first.Mesh.customDataIntVboId);
        Assert.False(original.IsPrepared);
        Assert.Throws<InvalidOperationException>(() => original.Inputs);
        // Replacement is explicit owner handoff, after the original resource's observed retirement.
        AccessTools.Field(typeof(MeshDataPool), "modelRef").SetValue(first.Pool, replacement.Mesh);
        prepared.Prepare([]);
        Assert.NotSame(original, prepared[first.Pool]);
        Assert.True(prepared[first.Pool].IsPrepared);
        Assert.False(original.IsPrepared);
        Assert.True(OpenTK.Graphics.OpenGL4.GL.IsVertexArray(replacement.Mesh.VaoId));
    }

    /// <summary>Equivalent pass values reuse target facts, but changed images, routing and disposal cannot.</summary>
    [Fact]
    public void TargetFactsFollowPublishedStorageAndPassInputs()
    {
        EnsureContextValid();
        using var image = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription
        {
            Dimension = TextureDimension.Two, Width = 2, Height = 2, Format = TextureFormat.R32f,
            MipLevels = 1, Storage = ResourceStorageMode.Mutable, BaseLevel = 0, MaxLevel = 0,
            Filter = TextureFilterMode.Nearest
        }).RequireResource();
        var pass = new RHIRenderPassDescription([new(Image: RHIAttachment.FromTexture(image))]);
        RHICommandContext.Current.DescribeRenderTargets(pass);
        var prepared = new LiquidTargetPreparation(pass);
        Assert.True(prepared.Matches(new([new(Image: RHIAttachment.FromTexture(image))])));
        Assert.False(prepared.Matches(new([new(Image: RHIAttachment.FromTexture(image), DiscardOutput: false)],
            actions: new(DepthLoad: AttachmentLoad.Clear))));
        image.Resize(3, 2);
        Assert.False(prepared.Matches(pass));
        var resized = new LiquidTargetPreparation(pass);
        Assert.True(resized.Matches(pass));
        image.Dispose();
        Assert.False(resized.Matches(pass));
    }

    /// <summary>Borrowed surfaces invalidate signatures when their attached image storage changes or they retire.</summary>
    [Fact]
    public void SurfacePreparationUsesPublishedAttachmentRevision()
    {
        EnsureContextValid();
        using var depth = new DepthTexture(1, 1, TextureFormat.DepthComponent24);
        using var target = DynamicGpuFramebuffer.CreateDepthOnly(depth)!;
        using var borrowed = DynamicGpuFramebuffer.Wrap(target.FboId, "Liquid.Preparation", 1, 1);
        borrowed.PublishRenderPassMetadata();
        var pass = new RHIRenderPassDescription(borrowed.SurfaceReference, LiquidPipelineStates.DepthOutputs);
        RHICommandContext.Current.DescribeRenderTargets(pass);
        var prepared = new LiquidTargetPreparation(pass);
        Assert.True(prepared.Matches(pass));
        depth.Resize(2, 1);
        Assert.False(prepared.Matches(pass));
        var resized = new LiquidTargetPreparation(pass);
        Assert.True(resized.Matches(pass));
        borrowed.Dispose();
        Assert.False(resized.Matches(pass));
    }
    #endregion

    #region Private
    /// <summary>Supplies the engine's actual private pool list without requiring a world or culling service.</summary>
    private static MeshDataPoolManager Manager(List<MeshDataPool> pools)
    {
        var manager = (MeshDataPoolManager)RuntimeHelpers.GetUninitializedObject(typeof(MeshDataPoolManager));
        AccessTools.Field(typeof(MeshDataPoolManager), "pools").SetValue(manager, pools);
        return manager;
    }
    #endregion
}
