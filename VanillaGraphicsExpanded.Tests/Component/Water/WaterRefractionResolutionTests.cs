using VanillaGraphicsExpanded.Rendering.RHI;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR.Liquids;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Shaders;


using VanillaGraphicsExpanded.Tests.Support.GPU.Native;
namespace VanillaGraphicsExpanded.Tests.Component.Water;


/// <summary>Checks publication generations and resource ownership across optional background reduction.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class WaterRefractionResolutionTests(HeadlessGLFixture fixture) : LumOnShaderFunctionalTestBase(fixture)
{
    #region Public API
    /// <summary>Reduction runs before publication, reuses storage, fails closed and retires its pair on resolution changes.</summary>
    [Fact]
    public void ResolutionChangesAndFailedReductionNeverPublishStaleData()
    {
        // Arrange: establish this scenario's controlled inputs and starting state.

        EnsureShaderTestAvailable();
        using var composite = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba16f, Width = System.Math.Max(1, 5), Height = System.Math.Max(1, 3), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        using var replacement = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba16f, Width = System.Math.Max(1, 7), Height = System.Math.Max(1, 5), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        using var scene = new WaterRefractionScene();
        var reduction = GpuShaderProgramLibraries.Declare(Programs.Api, WaterRefractionReductionShaderProgram.Contract.Identity, () => new WaterRefractionReductionShaderProgram());
        // Arrange: require the reduction program used by the lifecycle transitions below to be ready.
        // Assert: a shader setup failure should stop before testing scene publication.
        Assert.True(reduction.EnsureReady());
        int reductions = 0;
        // Act: publish a full-resolution source without reduction, then start a new reduced generation.
        var fullTarget = scene.BeginFrame(true, composite, 1)!;
        scene.SourceColor!.UploadDataImmediate(Enumerable.Range(0, 15).SelectMany(_ => new float[] { 8,2,.5f,1 }).ToArray());
        scene.SourceDepth!.UploadDataImmediate(Enumerable.Repeat(.75f, 15).ToArray());
        scene.Publish(Reduce);
        // Assert: a scale-one frame publishes directly without invoking the optional reducer.
        Assert.True(scene.Published);
        Assert.Equal(0, reductions);
        Assert.Equal(5, scene.Color!.Width);
        scene.BeginFrame(true, composite, 2);
        Assert.False(scene.Published);
        Assert.Equal(3, scene.Color!.Width);
        Assert.Equal(2, scene.Color.Height);
        // Arrange: the resize retired the prior source storage, so author fresh pixels for the reduced generation.
        // Fill the full restored source again: switching generations may retire old storage.
        scene.SourceColor!.UploadDataImmediate(Enumerable.Range(0, 15).SelectMany(_ => new float[] { 8,2,.5f,1 }).ToArray());
        scene.SourceDepth!.UploadDataImmediate(Enumerable.Repeat(.75f, 15).ToArray());
        var halfTarget = scene.BeginFrame(true, composite, 2);
        var halfColor = scene.Color;
        var halfDepth = scene.Depth;
        // Act: attempt publication without the optional reducer.
        scene.Publish();
        // Assert: a scaled frame cannot publish until reduction has completed.
        Assert.False(scene.Published);
        // Act: run the configured reducer against the current unpublished source pair.
        scene.Publish(Reduce);
        // Assert: one reduction publishes the half-size pair and preserves its storage on the next frame.
        Assert.True(scene.Published);
        Assert.Equal(1, reductions);
        Assert.Equal(8, scene.Color.ReadTexel()[0]);
        Assert.Same(halfTarget, scene.BeginFrame(true, composite, 2));
        Assert.Same(halfColor, scene.Color);
        Assert.Same(halfDepth, scene.Depth);
        Assert.Throws<InvalidOperationException>(() => scene.Publish((_, _, _) => throw new InvalidOperationException("Reduction failed")));
        Assert.False(scene.Published);
        // Act: retry publication after the injected reduction failure.
        scene.BeginFrame(true, composite, 2);
        scene.Publish(Reduce);
        // Assert: the failed reduction left a recoverable generation that can publish on retry.
        Assert.True(scene.Published);
        // Arrange: begin a fresh frame before exercising the shader library's reload lifecycle.
        // RegisterAll redeclares existing owners on shader reload. Reprepare that
        // actual library owner while retaining the immutable background allocation.
        scene.BeginFrame(true, composite, 2);
        var library = GpuShaderProgramLibraries.Require(Programs.Api);
        // Act: reload the library while redeclaring the same reduction program owner.
        library.BeginReload();
        Assert.Same(reduction, GpuShaderProgramLibraries.Declare(Programs.Api, WaterRefractionReductionShaderProgram.Contract.Identity, () => new WaterRefractionReductionShaderProgram()));
        library.CompleteReload();
        Assert.True(reduction.RequiresPreparation);
        Assert.False(scene.Published);
        Assert.True(reduction.EnsureReady());
        // Act: publish again with the same reduced destination after shader re-preparation.
        scene.Publish(Reduce);
        // Assert: re-preparation restores publication without replacing the allocated reduced pair.
        Assert.True(scene.Published);
        Assert.Same(halfColor, scene.Color); Assert.Same(halfDepth, scene.Depth);
        Assert.Equal(8, scene.Color!.ReadTexel()[0]);
        var context = RHICommandContext.Current;
        // Act: enter the render boundary and bind the full-resolution composite as its destination.
        using (var boundary = context.EnterBoundary(new("Tests.Water.ReductionSource", true,
            operations: BoundaryOperations.RenderPasses)))
            boundary.Run(() =>
            {
                context.BeginRenderPass(halfTarget!);
                try
                {
                    // Assert: the composite remains valid and full-sized while the reduction render pass is active.
                    Assert.True(composite.IsValid);
                    Assert.Equal(5, composite.Width);
                    Assert.Equal(3, composite.Height);
                }
                finally { context.EndRenderPass(); }
            });
        // Act: resize the composite only after the render pass restoration boundary, then switch destinations.
        // Replacing storage after boundary exit ensures the test exercises generation retirement, not in-pass mutation.
        composite.Resize(7, 5);
        scene.BeginFrame(true, composite, 2);
        Assert.False(halfColor.IsValid); Assert.False(halfDepth!.IsValid);
        scene.BeginFrame(true, replacement, 2);
        Assert.False(halfColor.IsValid);
        Assert.False(halfDepth!.IsValid);
        Assert.Equal(4, scene.Color!.Width);
        Assert.Equal(3, scene.Color.Height);
        scene.BeginFrame(true, replacement, 1);
        // Assert: every generation change retires stale reduced textures and publishes dimensions for its destination.
        Assert.Equal(7, scene.Color!.Width);
        // Act: invalidate and disable the scene after the replacement generation.
        scene.Invalidate();
        Assert.False(scene.Published);
        // Act and assert: disabling publication drops reduced resources while preserving the external composite.
        scene.BeginFrame(false, replacement, 2);
        Assert.Null(scene.Color); Assert.Null(scene.Depth);
        Assert.True(composite.IsValid);

        /// <summary>Executes the actual reduction program while asserting unpublished ownership and coherent full sources.</summary>
        void Reduce(RHIRenderPassDescription target, DynamicTexture2D color, DynamicTexture2D depth)
        {
            Assert.False(scene.Published);
            Assert.Same(scene.SourceColor, color); Assert.Same(scene.SourceDepth, depth);
            Assert.Equal(composite.Width, color.Width); Assert.Equal(composite.Height, depth.Height);
            reduction.SourceColor = color; reduction.SourceDepth = depth;
            TestFramework.RenderQuadTo(reduction, target);
            reductions++;
        }
    }
    #endregion
}
