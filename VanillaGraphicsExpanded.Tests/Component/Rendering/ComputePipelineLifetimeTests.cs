using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Tests.Support.GPU.Shaders;

namespace VanillaGraphicsExpanded.Tests.Component.Rendering;

/// <summary>Verifies compute pipeline disposal does not acquire or retire its shader owner.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class ComputePipelineLifetimeTests(HeadlessGLFixture fixture)
{
    #region Public API
    /// <summary>Pipeline wrappers remain independently disposable while shader retirement invalidates their access.</summary>
    [Fact]
    public void PipelineLifetimeDoesNotAcquireShaderOwnership()
    {
        // Arrange: create two labeled descriptions over the same shader in one live RHI context.
        fixture.EnsureContextValid();
        fixture.InitializeResourceDisposal();
        try
        {
            var context = RHICommandContext.Current;
            using var shader = context.CreateShader(RHIShaderInputs.ComputeDescription()).RequireResource();
            var firstDescription = new RHIComputePipelineDescription(shader, "First");
            var secondDescription = new RHIComputePipelineDescription(shader, "Second");
            Assert.Equal(firstDescription, secondDescription);
            Assert.Equal(firstDescription.GetHashCode(), secondDescription.GetHashCode());

            // Act: realize both pipeline owners, then retire only the first one.
            using var first = context.CreateComputePipeline(firstDescription).RequireResource();
            using var second = context.CreateComputePipeline(secondDescription).RequireResource();
            Assert.NotSame(first, second);
            first.Dispose();

            // Assert: retiring one pipeline leaves the shared shader and remaining pipeline usable.
            RHIShaderRef shaderReference = shader;
            Assert.Same(shader, shaderReference.RequireShader());
            Assert.Same(shader, second.RequireShader());
            shader.Dispose();
#if DEBUG
            Assert.Throws<ObjectDisposedException>(() => second.RequireShader());
#endif
        }
        finally
        {
            fixture.CleanupGpuResources();
            OpenGlStateCache.Current.InvalidateAll();
        }
    }
    #endregion
}
