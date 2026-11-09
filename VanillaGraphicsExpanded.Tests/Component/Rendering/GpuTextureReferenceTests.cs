using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.Tests.Component.Rendering;

/// <summary>Exercises consumer references across metadata changes and owner retirement.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class GpuTextureReferenceTests(HeadlessGLFixture fixture)
{
    #region Public API


    /// <summary>Owner replacement follows the wrapper while the old native view remains retired.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OwnerReplacementPreservesReferenceButRejectsOldDependencies(bool managed)
    {
        // Arrange: Create a consumer reference and dependencies for either managed or external owner replacement.
        // Act and assert: Replace the wrapper's owner and verify the reference follows it while old dependencies are rejected.
        fixture.EnsureContextValid();
        if (managed) fixture.InitializeResourceDisposal();
        try
        {
            using var owner = RHICommandContext.Current.CreateDynamicTexture3D(new RHITextureDescription { Dimension = TextureDimension.TwoArray, Format = TextureFormat.Rgba32f, Width = System.Math.Max(1, 2), Height = System.Math.Max(1, 2), Depth = System.Math.Max(1, 4), MipLevels = 1, Storage = (true ? ResourceStorageMode.Immutable : ResourceStorageMode.Mutable), Filter = TextureFilterMode.Linear, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
            using var view = RHICommandContext.Current.CreateTextureView(new RHITextureViewDescription { Source = owner, Dimension = TextureDimension.TwoArray, Format = TextureFormat.Rgba32f, BaseMip = 0, MipCount = 1, BaseLayer = 1, LayerCount = 2, Label = null }).RequireResource();
            GpuTextureRef reference = owner;
            GpuTextureRef viewReference = view;
            var before = reference;
            using (var image = GpuFramebufferAttachment.FromTexture(viewReference, layer: 1))
                Assert.Equal(view.TextureId, image.TextureId);
            Assert.Throws<ArgumentOutOfRangeException>(() => GpuFramebufferAttachment.FromTexture(viewReference, layer: 2));
            owner.Resize(4, 3, 4);
            Assert.Equal(before, reference);
            Assert.Equal(owner.TextureId, reference.TextureId);
            Assert.Equal(4, reference.RequireImage(0).Width);
            Assert.False(viewReference.IsValid);
            Assert.False(view.IsValid);
            Assert.Throws<InvalidOperationException>(() => viewReference.TextureId);
            Assert.False(RHIResourceManager.ValidateTextureReferenceInput(viewReference.Resource));
        }
        finally { fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }


#endregion
}
