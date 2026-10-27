using System;
using HarmonyLib;
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
        finally { fixture.CleanupGpuResources(); StateCache.Current.InvalidateAll(); }
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
        finally { fixture.CleanupGpuResources(); StateCache.Current.InvalidateAll(); }
    }
    /// <summary>Native callbacks cannot retire executing objects, and retired selections reject work before dispatch.</summary>
    [Fact]
    public void DispatchProtectsShaderAndPipelineUntilNativeReturn()
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        var harmony = new Harmony($"rhi-compute-lifetime-{Guid.NewGuid()}");
        try
        {
            var context = RHICommandContext.Current;
            using var shader = context.CreateShader(RHIShaderInputs.ComputeDescription()).RequireResource();
            using var pipeline = context.CreateComputePipeline(new(shader)).RequireResource();
            int calls = 0;
            DispatchObservation.Callback = () =>
            {
                calls++;
                Assert.Throws<InvalidOperationException>(shader.Dispose);
                Assert.Throws<InvalidOperationException>(pipeline.Dispose);
                Assert.Throws<InvalidOperationException>(() => context.Dispatch(new(0)));
                Assert.Throws<InvalidOperationException>(() => context.SetPipeline(pipeline));
            };
            harmony.Patch(AccessTools.Method(typeof(OpenTK.Graphics.OpenGL4.GL), "DispatchCompute", [typeof(int), typeof(int), typeof(int)]),
                prefix: new HarmonyMethod(typeof(DispatchObservation), nameof(DispatchObservation.BeforeDispatch)));
            context.SetPipeline(pipeline);
            // Zero workgroups exercise dispatch callbacks without running a shader whose resources are unbound.
            context.Dispatch(new(0));
            Assert.Equal(1, calls);
            pipeline.Dispose();
            Assert.Throws<ObjectDisposedException>(() => context.Dispatch(new(0)));
            Assert.Equal(1, calls);
            Assert.Same(shader, ((RHIShaderRef)shader).RequireShader());
        }
        finally
        {
            DispatchObservation.Callback = null;
            harmony.UnpatchAll(harmony.Id);
            fixture.CleanupGpuResources(); StateCache.Current.InvalidateAll();
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
            using var shader = context.CreateShader(RHIShaderInputs.ComputeDescription()).RequireResource();
            using var pipeline = context.CreateComputePipeline(new(shader)).RequireResource();
            context.SetPipeline(pipeline);
            context.Dispatch(new(0));
            pipeline.Dispose();
            shader.Dispose();
            using var replacement = context.CreateShader(RHIShaderInputs.ComputeDescription()).RequireResource();
            using var replacementPipeline = context.CreateComputePipeline(new(replacement)).RequireResource();
            using var boundary = context.EnterBoundary(new("Tests.RetiredCurrentShader", true, compute: [replacementPipeline]));
            boundary.Run(() => { context.SetPipeline(replacementPipeline); context.BindResources(new(replacement)); context.Dispatch(new(0)); });
            Assert.Equal(OpenTK.Graphics.OpenGL.ErrorCode.NoError, OpenTK.Graphics.OpenGL.GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); StateCache.Current.InvalidateAll(); }
    }
    #endregion

    #region Private
    /// <summary>Observes the exact managed backend entry point while keeping native dispatch intact.</summary>
    private static class DispatchObservation
    {
        internal static Action? Callback;
        /// <summary>Runs the test callback before native dispatch returns to the guarded submission.</summary>
        internal static void BeforeDispatch() => Callback?.Invoke();
    }
    #endregion
}
