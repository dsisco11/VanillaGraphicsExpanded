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
[Trait("Category", "GPU")]
public sealed class LiquidPreparationTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Public API
    /// <summary>Stable membership reuses adapters; growth, failed candidates, removal and retirement preserve ownership.</summary>
    [Fact]
    public void MembershipRefreshIsTransactionalAndRetainsUnchangedGeometry()
    {
        EnsureContextValid();
        using var first = new LiquidPoolStorage();
        using var second = new LiquidPoolStorage();
        using var prepared = new LiquidPoolPreparation();
        var pools = new List<MeshDataPool> { first.Pool };
        var manager = Manager(pools);
        prepared.Prepare([manager]);
        var original = prepared[first.Pool];
        prepared.Prepare([manager]);
        Assert.Same(original, prepared[first.Pool]);

        pools.Add(second.Pool);
        int stream = second.Mesh.customDataIntVboId;
        second.Mesh.customDataIntVboId = 0;
        Assert.Throws<InvalidOperationException>(() => prepared.Prepare([manager]));
        Assert.Same(original, prepared[first.Pool]);
        Assert.True(original.IsCurrent);
        second.Mesh.customDataIntVboId = stream;
        prepared.Prepare([manager]);
        Assert.Same(original, prepared[first.Pool]);
        var added = prepared[second.Pool];

        pools.Remove(first.Pool);
        prepared.Prepare([manager]);
        Assert.False(original.IsCurrent);
        Assert.Same(added, prepared[second.Pool]);
        Assert.Throws<InvalidOperationException>(() => prepared[first.Pool]);
        // A replacement manager with the same count must not reuse the previous membership.
        prepared.Prepare([Manager([first.Pool])]);
        Assert.False(added.IsCurrent);
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
        using var prepared = new LiquidPoolPreparation();
        var manager = Manager([first.Pool]);
        prepared.Prepare([manager]);
        var original = prepared[first.Pool];
        first.Pool.indicesSizes[0] = 6;
        Assert.Throws<InvalidOperationException>(() => original.CaptureDraws().ToArray());
        first.Pool.indicesSizes[0] = 3;
        int stream = first.Mesh.customDataIntVboId;
        first.Mesh.customDataIntVboId = 0;
        Assert.Throws<InvalidOperationException>(() => prepared.Prepare([manager]));
        Assert.Same(original, prepared[first.Pool]);
        first.Mesh.customDataIntVboId = stream;
        Assert.True(original.IsCurrent);
        AccessTools.Field(typeof(MeshDataPool), "modelRef").SetValue(first.Pool, second.Mesh);
        prepared.Prepare([manager]);
        Assert.NotSame(original, prepared[first.Pool]);
        Assert.False(original.IsCurrent);
        Assert.True(prepared[first.Pool].IsCurrent);
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
        Assert.Throws<InvalidOperationException>(() => resized.Matches(pass));
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
