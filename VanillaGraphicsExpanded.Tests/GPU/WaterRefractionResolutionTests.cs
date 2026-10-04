using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR.Liquids;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Shaders;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks publication generations and resource ownership across optional background reduction.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class WaterRefractionResolutionTests(HeadlessGLFixture fixture) : LumOnShaderFunctionalTestBase(fixture)
{
    #region Public API
    /// <summary>Reduction runs before publication, reuses storage, fails closed and retires its pair on resolution changes.</summary>
    [Fact]
    public void ResolutionChangesAndFailedReductionNeverPublishStaleData()
    {
        EnsureShaderTestAvailable();
        using var scene = new WaterRefractionScene();
        using var composite = DynamicTexture2D.Create(5, 3, PixelInternalFormat.Rgba16f);
        var reduction = GpuShaderPrograms.Declare(Programs.Api, new WaterRefractionReductionShaderProgram());
        Assert.True(reduction.EnsureReady());
        int reductions = 0;
        var fullTarget = scene.BeginFrame(true, composite, 1)!;
        scene.SourceColor!.UploadDataImmediate(Enumerable.Range(0, 15).SelectMany(_ => new float[] { 8,2,.5f,1 }).ToArray());
        scene.SourceDepth!.UploadDataImmediate(Enumerable.Repeat(.75f, 15).ToArray());
        scene.Publish(Reduce);
        Assert.True(scene.Published);
        Assert.Equal(0, reductions);
        Assert.Equal(5, scene.Color!.Width);
        scene.BeginFrame(true, composite, 2);
        Assert.False(scene.Published);
        Assert.Equal(3, scene.Color!.Width);
        Assert.Equal(2, scene.Color.Height);
        // Fill the full restored source again: switching generations may retire old storage.
        scene.SourceColor!.UploadDataImmediate(Enumerable.Range(0, 15).SelectMany(_ => new float[] { 8,2,.5f,1 }).ToArray());
        scene.SourceDepth!.UploadDataImmediate(Enumerable.Repeat(.75f, 15).ToArray());
        var halfTarget = scene.BeginFrame(true, composite, 2);
        var halfColor = scene.Color;
        var halfDepth = scene.Depth;
        scene.Publish();
        Assert.False(scene.Published);
        scene.Publish(Reduce);
        Assert.True(scene.Published);
        Assert.Equal(1, reductions);
        Assert.Equal(8, scene.Color.ReadPixels()[0]);
        Assert.Same(halfTarget, scene.BeginFrame(true, composite, 2));
        Assert.Same(halfColor, scene.Color);
        Assert.Same(halfDepth, scene.Depth);
        Assert.Throws<InvalidOperationException>(() => scene.Publish((_, _, _) => throw new InvalidOperationException("Reduction failed")));
        Assert.False(scene.Published);
        scene.BeginFrame(true, composite, 2);
        scene.Publish(Reduce);
        Assert.True(scene.Published);
        // RegisterAll redeclares existing owners on shader reload. Reprepare that
        // actual library owner while retaining the immutable background allocation.
        scene.BeginFrame(true, composite, 2);
        Assert.Same(reduction, GpuShaderPrograms.Declare(Programs.Api, new WaterRefractionReductionShaderProgram()));
        Assert.True(reduction.RequiresPreparation);
        Assert.False(scene.Published);
        Assert.True(reduction.EnsureReady());
        scene.Publish(Reduce);
        Assert.True(scene.Published);
        Assert.Same(halfColor, scene.Color); Assert.Same(halfDepth, scene.Depth);
        Assert.Equal(8, scene.Color!.ReadPixels()[0]);
        Assert.True(composite.Resize(7, 5));
        scene.BeginFrame(true, composite, 2);
        Assert.False(halfColor.IsValid);
        Assert.False(halfDepth!.IsValid);
        Assert.Equal(4, scene.Color!.Width);
        Assert.Equal(3, scene.Color.Height);
        scene.BeginFrame(true, composite, 1);
        Assert.Equal(7, scene.Color!.Width);
        scene.Invalidate();
        Assert.False(scene.Published);
        scene.BeginFrame(false, composite, 2);
        Assert.Null(scene.Color); Assert.Null(scene.Depth);
        Assert.True(composite.IsValid);

        /// <summary>Executes the actual reduction program while asserting unpublished ownership and coherent full sources.</summary>
        void Reduce(GpuFramebuffer target, DynamicTexture2D color, DynamicTexture2D depth)
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
