using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Verifies explicit GPU ownership and allocation-failure cleanup.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class GpuResourceCollectionTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Ownership tests

    /// <summary>Disposing a collection retires framebuffers before other resources and preserves borrowed textures.</summary>
    [Fact]
    public void DisposalRetiresFramebuffersFirstAndPreservesBorrowedTextures()
    {
        EnsureContextValid();
        using var resources = new GpuResourceCollection();
        using var borrowed = DynamicTexture2D.Create(2, 2, PixelInternalFormat.Rgba16f);
        var owned = resources.Own(DynamicTexture2D.Create(2, 2, PixelInternalFormat.Rgba16f));
        GpuFramebuffer? framebuffer = null;
        var observation = resources.Own(new DisposalObservation(() => Assert.True(framebuffer!.IsDisposed)));
        framebuffer = resources.Own(GpuFramebuffer.CreateMRT([owned, borrowed], ownsTextures: false)!);
        int framebufferId = framebuffer.FboId;
        int textureId = owned.TextureId;

        // Registering the same owner twice must remain harmless, including repeated collection disposal.
        Assert.Same(owned, resources.Own(owned));
        resources.Dispose();
        resources.Dispose();

        Assert.Equal(1, observation.DisposalCount);
        Assert.True(owned.IsDisposed);
        Assert.False(GL.IsFramebuffer(framebufferId));
        Assert.False(GL.IsTexture(textureId));
        Assert.False(borrowed.IsDisposed);
        Assert.True(GL.IsTexture(borrowed.TextureId));
    }

    /// <summary>An allocation exception releases resources already registered by the construction scope.</summary>
    [Fact]
    public void FailedConstructionScopeReleasesPartialAllocation()
    {
        EnsureContextValid();
        DynamicTexture2D? allocated = null;
        int textureId = 0;
        Assert.Throws<InvalidOperationException>((Action)(() =>
        {
            using var resources = new GpuResourceCollection();
            allocated = resources.Own(DynamicTexture2D.Create(2, 2, PixelInternalFormat.Rgba16f));
            textureId = allocated.TextureId;
            throw new InvalidOperationException("Simulated later allocation failure.");
        }));
        Assert.True(allocated!.IsDisposed);
        Assert.False(GL.IsTexture(textureId));
    }

    /// <summary>Rejected registration does not silently acquire ownership of the caller's resource.</summary>
    [Fact]
    public void InvalidRegistrationLeavesCallerOwnershipIntact()
    {
        using var resources = new GpuResourceCollection();
        Assert.Throws<ArgumentNullException>(() => resources.Own<DisposalObservation>(null!));
        resources.Dispose();
        using var candidate = new DisposalObservation(() => { });
        Assert.Throws<ObjectDisposedException>(() => resources.Own(candidate));
        Assert.False(candidate.IsDisposed);
    }

    /// <summary>A failing resource release does not prevent the remaining owned resources from being retired.</summary>
    [Fact]
    public void DisposalFailureStillRetiresRemainingResources()
    {
        using var resources = new GpuResourceCollection();
        var failed = resources.Own(new DisposalObservation(() => throw new InvalidOperationException("Release failure.")));
        var remaining = resources.Own(new DisposalObservation(() => { }));
        var error = Assert.Throws<AggregateException>(() => resources.Dispose());
        Assert.IsType<InvalidOperationException>(Assert.Single(error.InnerExceptions));
        Assert.True(failed.IsDisposed);
        Assert.True(remaining.IsDisposed);
        Assert.Equal(1, remaining.DisposalCount);
        resources.Dispose();
    }

    #endregion

    #region Disposal observer

    /// <summary>Observes disposal order without allocating an OpenGL handle.</summary>
    private sealed class DisposalObservation(Action onDispose) : GpuResource
    {
        public int DisposalCount { get; private set; }
        protected override nint ResourceId { get; set; }
        protected override GpuResourceKind ResourceKind => GpuResourceKind.Texture;

        /// <summary>No handle exists to label.</summary>
        public override void SetDebugName(string? debugName) { }

        /// <summary>Checks surrounding resource state at the point disposal begins.</summary>
        protected override void OnBeforeDelete(nint id)
        {
            DisposalCount++;
            onDispose();
        }
    }

    #endregion
}
