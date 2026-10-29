using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;
using System;
using System.Runtime.InteropServices;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Verifies compute realization identity and independent shader/pipeline ownership.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class RHIComputePipelineTests(HeadlessGLFixture fixture)
{
    #region Public API
    /// <summary>Labels do not alter behavior identity, while disposal and shader retirement remain independent.</summary>
    [Fact]
    public void PipelineLifetimeDoesNotAcquireShaderOwnership()
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        try
        {
            var context = RHICommandContext.Current;
            using var shader = context.CreateShader(RHIShaderInputs.ComputeDescription()).RequireResource();
            var firstDescription = new RHIComputePipelineDescription(shader, "First");
            var secondDescription = new RHIComputePipelineDescription(shader, "Second");
            Assert.Equal(firstDescription, secondDescription);
            Assert.Equal(firstDescription.GetHashCode(), secondDescription.GetHashCode());
            using var first = context.CreateComputePipeline(firstDescription).RequireResource();
            using var second = context.CreateComputePipeline(secondDescription).RequireResource();
            Assert.NotSame(first, second);
            first.Dispose();
            RHIShaderRef shaderReference = shader;
            Assert.Same(shader, shaderReference.RequireShader());
            Assert.Same(shader, second.RequireShader());
            shader.Dispose();
            Assert.Throws<ObjectDisposedException>(() => second.RequireShader());
        }
        finally { fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }
    /// <summary>Equivalent code in independent live executables never collapses their generation identities.</summary>
    [Fact]
    public void DescriptionsDistinguishIndependentExecutableGenerations()
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        try
        {
            var context = RHICommandContext.Current;
            var description = RHIShaderInputs.ComputeDescription();
            using var first = context.CreateShader(description).RequireResource();
            using var second = context.CreateShader(description).RequireResource();
            Assert.NotEqual(new RHIComputePipelineDescription(first), new RHIComputePipelineDescription(second));
        }
        finally { fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }
    /// <summary>Native callbacks cannot retire executing objects, and retired selections reject work before dispatch.</summary>
    [Fact]
    public unsafe void DispatchProtectsShaderAndPipelineUntilNativeReturn()
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        try
        {
            var context = RHICommandContext.Current;
            using var shader = context.CreateShader(RHIShaderInputs.EmptyComputeDescription()).RequireResource();
            using var pipeline = context.CreateComputePipeline(new(shader)).RequireResource();
            int calls = 0;
            using var native = new NativeDriverCapture();
            nint original = native.Original("glDispatchCompute");
            native.Add("glDispatchCompute", new DispatchCompute((x, y, z) =>
            {
                native.Observe(() =>
                {
                    calls++;
                    Assert.Throws<InvalidOperationException>(shader.Dispose);
                    Assert.Throws<InvalidOperationException>(pipeline.Dispose);
                    Assert.Throws<InvalidOperationException>(() => context.Dispatch(new(0)));
                    Assert.Throws<InvalidOperationException>(() => context.SetPipeline(pipeline));
                });
                ((delegate* unmanaged[Stdcall]<uint, uint, uint, void>)original)(x, y, z);
            }));
            native.Install();
            context.SetPipeline(pipeline);
            context.BindResources(new(shader));
            // Zero workgroups exercise dispatch callbacks without running a shader whose resources are unbound.
            context.Dispatch(new(0));
            native.RequireSuccessfulObservation();
            Assert.Equal(1, calls);
            pipeline.Dispose();
            Assert.Throws<ObjectDisposedException>(() => context.Dispatch(new(0)));
            Assert.Equal(1, calls);
            Assert.Same(shader, ((RHIShaderRef)shader).RequireShader());
        }
        finally
        {
            fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll();
        }
    }
    /// <summary>A deleted current executable cannot be captured as a restorable name after another program frees it.</summary>
    [Fact]
    public void RetiredCurrentShaderCannotBecomeScopeRestorationTarget()
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        try
        {
            var context = RHICommandContext.Current;
            using var shader = context.CreateShader(RHIShaderInputs.EmptyComputeDescription()).RequireResource();
            using var pipeline = context.CreateComputePipeline(new(shader)).RequireResource();
            context.SetPipeline(pipeline);
            context.BindResources(new(shader));
            context.Dispatch(new(0));
            pipeline.Dispose();
            shader.Dispose();
            using var replacement = context.CreateShader(RHIShaderInputs.EmptyComputeDescription()).RequireResource();
            using var replacementPipeline = context.CreateComputePipeline(new(replacement)).RequireResource();
            using var boundary = context.EnterBoundary(new("Tests.RetiredCurrentShader", true, compute: [replacementPipeline]));
            boundary.Run(() => { context.SetPipeline(replacementPipeline); context.BindResources(new(replacement)); context.Dispatch(new(0)); });
            Assert.Equal(OpenTK.Graphics.OpenGL.ErrorCode.NoError, OpenTK.Graphics.OpenGL.GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }
    /// <summary>Fixed pipeline admission rejects foreign threads and retired contexts without touching their native resources.</summary>
    [Fact]
    public void FixedRealizationsRequireTheirOriginalExecutionDomain()
    {
        fixture.EnsureContextValid();
        var domain = new object();
        VanillaGraphicsExpanded.Rendering.Integration.RenderContextRegistry.RegisterCurrent(domain, static _ => true);
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
            var worker = new System.Threading.Thread(() =>
            {
                try
                {
                Assert.Throws<InvalidOperationException>(() => computePipeline.RequireShader());
                Assert.Throws<InvalidOperationException>(() => graphicsPipeline.RequireShader());
                }
                catch (Exception error) { threadFailure = error; }
            });
            worker.Start(); worker.Join();
            Assert.Null(threadFailure);
            Assert.Same(compute, computePipeline.RequireShader());
            Assert.Same(graphics, graphicsPipeline.RequireShader());
            VanillaGraphicsExpanded.Rendering.Integration.RenderContextRegistry.Retire(domain);
            Assert.Throws<InvalidOperationException>(() => computePipeline.RequireShader());
            Assert.Throws<InvalidOperationException>(() => graphicsPipeline.RequireShader());
        }
        finally
        {
            VanillaGraphicsExpanded.Rendering.Integration.RenderContextRegistry.Retire(domain);
            VanillaGraphicsExpanded.Rendering.Integration.RenderContextRegistry.RegisterCurrent(fixture, static _ => true);
            OpenGlStateCache.Current.InvalidateAll();
        }
    }
    #endregion

    #region Private
    /// <summary>Preserves the native workgroup-count ABI during guarded lifetime assertions.</summary>
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void DispatchCompute(uint x, uint y, uint z);
    #endregion
}
