using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Integration;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

namespace VanillaGraphicsExpanded.Tests.Component.Rendering;

/// <summary>Verifies fixed pipeline owners enforce their original execution domain.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class RHIComputePipelineTests(HeadlessGLFixture fixture)
{
    #region Public API
    /// <summary>Foreign threads and retired context registrations cannot use fixed pipeline realizations.</summary>
    [Fact]
    public void FixedRealizationsRequireTheirOriginalExecutionDomain()
    {
        // Arrange: realize compute and graphics owners in a tracked origin domain.
        fixture.EnsureContextValid();
        var domain = new object();
        RenderContextRegistry.RegisterCurrent(domain, static _ => true);
        try
        {
            var context = RHICommandContext.Current;
            using var compute = context.CreateShader(new(ShaderKind.Compute,
                [new(ShaderStage.Compute, "#version 450\nlayout(local_size_x=1) in; void main() {}")], new())).RequireResource();
            using var graphics = context.CreateShader(new(ShaderKind.Graphics,
                [new(ShaderStage.Vertex, "#version 450\nvoid main() { gl_Position=vec4(0,0,0,1); }"),
                 new(ShaderStage.Fragment, "#version 450\nlayout(location=0) out vec4 color; void main() { color=vec4(1); }")], new())).RequireResource();
            using var computePipeline = context.CreateComputePipeline(new(compute)).RequireResource();
            using var graphicsPipeline = context.CreateGraphicsPipeline(new(graphics, new([]),
                new([new(TextureFormat.Rgba8)]), DynamicPipelineState.Viewport)).RequireResource();
            Exception? threadFailure = null;
            // Act: request fixed-pipeline shader resolution from a foreign worker thread.
            var worker = new System.Threading.Thread(() =>
            {
                try
                {
                    Assert.Throws<InvalidOperationException>(() => computePipeline.RequireShader());
                    Assert.Throws<InvalidOperationException>(() => graphicsPipeline.RequireShader());
                }
                catch (Exception error)
                {
                    threadFailure = error;
                }
            });
            worker.Start();
            worker.Join();

            // Assert: the worker thread rejected both owner lookups without throwing outside the assertions.
            Assert.Null(threadFailure);
            Assert.Same(compute, computePipeline.RequireShader());
            Assert.Same(graphics, graphicsPipeline.RequireShader());

            // Act: retire the shared execution domain after verifying the live owners.
            RenderContextRegistry.Retire(domain);

            // Assert: neither fixed pipeline exposes its shader through a retired origin.
            Assert.Throws<InvalidOperationException>(() => computePipeline.RequireShader());
            Assert.Throws<InvalidOperationException>(() => graphicsPipeline.RequireShader());
        }
        finally
        {
            RenderContextRegistry.Retire(domain);
            fixture.RestoreContextRegistration();
            OpenGlStateCache.Current.InvalidateAll();
        }
    }
    #endregion
}
