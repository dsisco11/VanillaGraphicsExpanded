using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

namespace VanillaGraphicsExpanded.Tests.Component.Rendering;

/// <summary>Verifies the metadata admission changes made when framebuffer ownership is retired.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class GpuResourceRetirementRegressionTests(HeadlessGLFixture fixture)
{
    #region Public API
    /// <summary>Retired manager metadata cannot preserve immutable framebuffer admission.</summary>
    [Fact]
    public void DeletedFramebufferWithdrawsImmutableConfigurationBeforeWrapperDisposal()
    {
        // Arrange: build immutable framebuffer state through the managed resource wrappers.
        fixture.EnsureContextValid();
        fixture.InitializeResourceDisposal();
        try
        {
            using var image = new GpuFramebufferAttachment(2, 2, TextureFormat.Rgba8);
            using var target = StaticGpuFramebuffer.Create([image]);
            int id = target.FboId;
            var cache = OpenGlStateCache.Current;
            Assert.Throws<InvalidOperationException>(() => cache.RequireMutableFramebufferConfiguration(id));

            // Act: apply the observed external deletion to the wrapper manager before disposal.
            GL.DeleteFramebuffer(id);
            RHIResourceManagerSystem.CurrentManager!.ObserveDeletion(GpuResourceKind.Framebuffer, id);

            // Assert: the retired association no longer reserves the immutable routing state.
            Assert.False(target.IsValid);
            cache.RequireMutableFramebufferConfiguration(id);
            Assert.False(cache.ValidateImmutableFramebufferRouting(id, [DrawBuffersEnum.ColorAttachment0]));
        }
        finally
        {
            fixture.CleanupGpuResources();
            OpenGlStateCache.Current.InvalidateAll();
        }
    }
    #endregion
}
