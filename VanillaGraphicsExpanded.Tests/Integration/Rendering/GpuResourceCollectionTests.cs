using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Tests.Integration.Rendering;

/// <summary>Verifies explicit GPU ownership and allocation-failure cleanup.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class GpuResourceCollectionTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Public API

    /// <summary>Disposing a collection retires framebuffers before other resources and preserves borrowed textures.</summary>
    [Fact]
    public void DisposalRetiresFramebuffersFirstAndPreservesBorrowedTextures()
    {
        // Arrange: make one owned texture, one borrowed attachment, and an observer that sees framebuffer disposal order.
        EnsureContextValid();
        using var resources = new GpuResourceCollection();
        using var borrowed = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba16f, Width = System.Math.Max(1, 2), Height = System.Math.Max(1, 2), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        var owned = resources.Own(RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba16f, Width = System.Math.Max(1, 2), Height = System.Math.Max(1, 2), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource());
        GpuFramebuffer? framebuffer = null;
        var observation = resources.Own(new DisposalObservation(() => Assert.True(framebuffer!.IsDisposed)));
        framebuffer = resources.Own(DynamicGpuFramebuffer.CreateMRT([owned, borrowed])!);
        int framebufferId = framebuffer.FboId;
        int textureId = owned.TextureId;

        // Act: duplicate ownership registration is idempotent, and disposal is safe to repeat.
        Assert.Same(owned, resources.Own(owned));
        resources.Dispose();
        resources.Dispose();

        // Assert: owned native names are retired after the framebuffer while the borrowed texture remains live.
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
        // Arrange and act: model a later construction failure after a native texture entered the scope.
        EnsureContextValid();
        DynamicTexture2D? allocated = null;
        int textureId = 0;
        Assert.Throws<InvalidOperationException>((Action)(() =>
        {
            using var resources = new GpuResourceCollection();
            allocated = resources.Own(RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba16f, Width = System.Math.Max(1, 2), Height = System.Math.Max(1, 2), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource());
            textureId = allocated.TextureId;
            throw new InvalidOperationException("Simulated later allocation failure.");
        }));
        // Assert: unwinding the construction scope retires the partial allocation.
        Assert.True(allocated!.IsDisposed);
        Assert.False(GL.IsTexture(textureId));
    }

    #endregion

    #region Private

    /// <summary>Observes disposal order without allocating an OpenGL handle.</summary>
    private sealed class DisposalObservation(Action onDispose) : GpuResource
    {
        #region Public API
        /// <summary>Gets the number of attempted releases.</summary>
        public int DisposalCount { get; private set; }
        /// <summary>Gets the current synthetic resource identifier.</summary>
        public override nint ResourceId { get; protected set; }
        /// <summary>No handle exists to label.</summary>
        public override void SetDebugName(string? debugName) { }
        #endregion

        #region Protected
        protected override GpuResourceKind ResourceKind => GpuResourceKind.Texture;
        /// <summary>Checks surrounding resource state at the point disposal begins.</summary>
        protected override void OnBeforeDelete(nint id)
        {
            DisposalCount++;
            onDispose();
        }
        #endregion
    }

    #endregion
}
