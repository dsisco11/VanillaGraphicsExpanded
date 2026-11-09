using System.Runtime.CompilerServices;
using HarmonyLib;
using VanillaGraphicsExpanded.PBR.Liquids;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Tests.Integration.Water.Fixtures;
using Vintagestory.API.Client;

namespace VanillaGraphicsExpanded.Tests.Integration.Water;

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
        // Arrange prepared geometry against the pool's current native vertex binding.
        EnsureContextValid();
        using var storage = new LiquidPoolStorage();
        using var prepared = new LiquidGeometryPreparation();
        var manager = Manager([storage.Pool]);
        prepared.Prepare([manager]);
        var original = prepared[storage.Pool];
        // Act by changing the real VAO binding.
        OpenTK.Graphics.OpenGL4.GL.VertexArrayVertexBuffer(storage.Mesh.VaoId, 0, storage.Mesh.xyzVboId, IntPtr.Zero, 12);
        // Assert the old receipt expires, then prepare the same pool membership again.
        Assert.False(original.IsPrepared);
        prepared.Prepare([manager]);
        // Assert preparation publishes a new receipt despite unchanged membership.
        Assert.NotSame(original, prepared[storage.Pool]);
        Assert.True(prepared[storage.Pool].IsPrepared);
    }

    /// <summary>Canonical buffer changes invalidate retained geometry without requiring a membership change.</summary>
    [Fact]
    public void ResourcePublicationInvalidatesPreparation()
    {
        // Arrange a valid preparation and retain its receipt before changing canonical native storage.
        EnsureContextValid();
        using var storage = new LiquidPoolStorage();
        using var prepared = new LiquidGeometryPreparation();
        var manager = Manager([storage.Pool]);
        prepared.Prepare([manager]);
        var original = prepared[storage.Pool];
        // Make the native stream too small. Its existing recipient receives the committed facts;
        // the next pre-boundary preparation must reject it rather than silently reuse an old receipt.
        // Act by publishing undersized storage.
        OpenTK.Graphics.OpenGL4.GL.NamedBufferData(storage.Mesh.customDataIntVboId, 4, IntPtr.Zero,
            OpenTK.Graphics.OpenGL4.BufferUsageHint.StaticDraw);
        // Assert the invalid candidate is rejected without replacing the previously retained receipt.
        Assert.ThrowsAny<Exception>(() => prepared.Prepare([manager]));
        Assert.Same(original, prepared[storage.Pool]);
        // Act by restoring valid storage and retrying preparation.
        OpenTK.Graphics.OpenGL4.GL.NamedBufferData(storage.Mesh.customDataIntVboId, 24, IntPtr.Zero,
            OpenTK.Graphics.OpenGL4.BufferUsageHint.StaticDraw);
        prepared.Prepare([manager]);
        // Assert the repaired candidate publishes a prepared receipt.
        Assert.True(prepared[storage.Pool].IsPrepared);
    }

    /// <summary>The installed disposal hook expires borrowers before invoking engine mesh deletion.</summary>
    [Fact]
    public void EngineDisposalRetiresGeometryBeforeDeleteMesh()
    {
        // Arrange a prepared borrower and observe engine mesh deletion through the installed hook.
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
            // Act by disposing the pool with the production retirement hook installed.
            harmony.CreateClassProcessor(typeof(VanillaGraphicsExpanded.HarmonyPatches.LiquidGeometryPoolRetirementHook)).Patch();
            storage.Pool.Dispose(api.Object);
            // Assert the receipt was retired before deletion and cannot be retrieved afterward.
            Assert.Throws<InvalidOperationException>(() => prepared[storage.Pool]);
            render.Verify(r => r.DeleteMesh(storage.Mesh), Moq.Times.Once);
        }
        finally { harmony.UnpatchAll(harmony.Id); }
    }

    /// <summary>Stable membership reuses adapters; growth, failed candidates, removal and retirement preserve ownership.</summary>
    [Fact]
    public void MembershipRefreshIsTransactionalAndRetainsUnchangedGeometry()
    {
        // Arrange one active pool and retain its first prepared adapter.
        EnsureContextValid();
        using var first = new LiquidPoolStorage();
        using var second = new LiquidPoolStorage();
        using var prepared = new LiquidGeometryPreparation();
        var pools = new List<MeshDataPool> { first.Pool };
        var manager = Manager(pools);
        prepared.Prepare([manager]);
        var original = prepared[first.Pool];
        // Act on stable membership before introducing an invalid second candidate.
        prepared.Prepare([manager]);
        // Assert unchanged membership preserves adapter identity.
        Assert.Same(original, prepared[first.Pool]);

        // Act by adding a pool with invalid storage and attempting a transactional refresh.
        pools.Add(second.Pool);
        VanillaGraphicsExpanded.HarmonyPatches.LiquidGeometryPoolAdditionHook.Finalizer(pools, 1);
        int stream = second.Mesh.customDataIntVboId;
        second.Mesh.customDataIntVboId = 0;
        Assert.Throws<InvalidOperationException>(() => prepared.Prepare([manager]));
        Assert.Same(original, prepared[first.Pool]);
        Assert.True(original.IsPrepared);
        // Act by repairing storage and retrying the candidate refresh.
        second.Mesh.customDataIntVboId = stream;
        prepared.Prepare([manager]);
        // Assert the prior adapter remains and the new pool is now published.
        Assert.Same(original, prepared[first.Pool]);
        var added = prepared[second.Pool];

        // Act by removing and retiring the first pool, then refreshing the remaining membership.
        pools.Remove(first.Pool);
        LiquidGeometryLifecycle.Retire(first.Pool);
        prepared.Prepare([manager]);
        // Assert retirement withdraws only the removed adapter and keeps the remaining one.
        Assert.False(original.IsPrepared);
        Assert.Same(added, prepared[second.Pool]);
        Assert.Throws<InvalidOperationException>(() => prepared[first.Pool]);
        // Act by notifying a collection replacement and preparing a new manager with the same count.
        LiquidGeometryLifecycle.ChangedCollections();
        prepared.Prepare([Manager([first.Pool])]);
        // Assert the new manager cannot reuse the old membership adapter.
        Assert.False(added.IsPrepared);
        Assert.NotSame(original, prepared[first.Pool]);
        // Act by disposing the preparation owner.
        prepared.Dispose();
        // Assert disposing the adapter owner leaves engine-owned meshes intact.
        Assert.False(first.Mesh.Disposed);
        Assert.False(second.Mesh.Disposed);
    }

    /// <summary>Changed geometry is rebuilt outside submission, while invalid replacements leave the old adapter installed.</summary>
    [Fact]
    public void GeometryRefreshPreservesCandidateFailureAndDynamicIntervals()
    {
        // Arrange one prepared adapter and retain its identity across mutations of the native pool data.
        EnsureContextValid();
        using var first = new LiquidPoolStorage();
        using var second = new LiquidPoolStorage();
        using var prepared = new LiquidGeometryPreparation();
        var manager = Manager([first.Pool]);
        prepared.Prepare([manager]);
        var original = prepared[first.Pool];
        // Act by changing the dynamic index count.
        first.Pool.indicesSizes[0] = 6;
        // Assert invalid ranges and candidates are rejected without replacing the prior adapter.
        Assert.Throws<InvalidOperationException>(() => original.CaptureDraws().ToArray());
        // Act by restoring the count and publishing undersized backing storage.
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
        // Arrange two prepared pools so the untouched peer can witness local refresh behavior.
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
        // Act by changing one pool's native configuration or committed storage without a topology notification.
        if (storageChange)
            OpenTK.Graphics.OpenGL4.GL.NamedBufferData(first.Mesh.customDataIntVboId, 24, IntPtr.Zero,
                OpenTK.Graphics.OpenGL4.BufferUsageHint.StaticDraw);
        else
            OpenTK.Graphics.OpenGL4.GL.VertexArrayVertexBuffer(first.Mesh.VaoId, 0, first.Mesh.xyzVboId, IntPtr.Zero, 12);
        Assert.False(original.IsPrepared);
        Assert.True(peer.IsPrepared);
        Assert.Throws<InvalidOperationException>(() => original.CaptureDraws().ToArray());
        // No topology notification occurred, so the supplied row is unused: only queued pools refresh.
        // Assert the next preparation replaces only the withdrawn pool and retains its peer.
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
        // Arrange: Prepare an adapter over the original mesh and retain a distinct live replacement mesh.
        EnsureContextValid();
        using var first = new LiquidPoolStorage();
        using var replacement = new LiquidPoolStorage();
        using var prepared = new LiquidGeometryPreparation();
        prepared.Prepare([Manager([first.Pool])]);
        var original = prepared[first.Pool];
        // Act: Delete one original native resource, then explicitly hand the pool over to replacement storage.
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
        // Assert: The original adapter stays retired and the new adapter targets the replacement native mesh.
        Assert.True(OpenTK.Graphics.OpenGL4.GL.IsVertexArray(replacement.Mesh.VaoId));
    }

    /// <summary>Equivalent pass values reuse target facts, but changed images, routing and disposal cannot.</summary>
    [Fact]
    public void TargetFactsFollowPublishedStorageAndPassInputs()
    {
        // Arrange a published target and capture the preparation's original pass facts.
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
        // Act and assert equivalent pass values match, while altered routing and attachment publication do not.
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
        // Arrange a borrowed framebuffer view and prepare its current published depth attachment.
        EnsureContextValid();
        using var depth = new DepthTexture(1, 1, TextureFormat.DepthComponent24);
        using var target = DynamicGpuFramebuffer.CreateDepthOnly(depth)!;
        using var borrowed = DynamicGpuFramebuffer.Wrap(target.FboId, "Liquid.Preparation", 1, 1);
        borrowed.PublishRenderPassMetadata();
        var pass = new RHIRenderPassDescription(borrowed.SurfaceReference, LiquidPipelineStates.DepthOutputs);
        RHICommandContext.Current.DescribeRenderTargets(pass);
        var prepared = new LiquidTargetPreparation(pass);
        // Act and assert attachment resize and retirement invalidate the prepared revision.
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
