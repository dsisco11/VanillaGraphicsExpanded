using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;
using System;
using System.Runtime.InteropServices;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Tests.Support.GPU.Native;
using VanillaGraphicsExpanded.Tests.Support.GPU.Shaders;

namespace VanillaGraphicsExpanded.Tests.Integration.Rendering;

/// <summary>Verifies compute realization identity and independent shader/pipeline ownership.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class RHIComputePipelineTests(HeadlessGLFixture fixture)
{
    #region Public API
    /// <summary>Equivalent code in independent live executables never collapses their generation identities.</summary>
    [Fact]
    public void DescriptionsDistinguishIndependentExecutableGenerations()
    {
        // Arrange: author one shader description and create two independent native executables.
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        try
        {
            var context = RHICommandContext.Current;
            var description = RHIShaderInputs.ComputeDescription();
            // Act: realize two independent native executables from the same description.
            using var first = context.CreateShader(description).RequireResource();
            using var second = context.CreateShader(description).RequireResource();
            // Assert: pipeline descriptions preserve each live executable's distinct generation identity.
            Assert.NotEqual(new RHIComputePipelineDescription(first), new RHIComputePipelineDescription(second));
        }
        finally { fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }
    /// <summary>Compliant owners keep resources live without guards; recursive submission remains an operation error.</summary>
    [Fact]
    public unsafe void DispatchReliesOnOwnerLifetimesAndRejectsRecursiveSubmission()
    {
        // Arrange: observe the real native dispatch while its managed pipeline owner remains live.
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
                    Assert.False(pipeline.IsDisposed);
                    Assert.Throws<InvalidOperationException>(() => context.Dispatch(new(0)));
                    Assert.Throws<InvalidOperationException>(() => context.SetPipeline(pipeline));
                });
                ((delegate* unmanaged[Stdcall]<uint, uint, uint, void>)original)(x, y, z);
            }));
            native.Install();
            context.SetPipeline(pipeline);
            context.BindResources(new(shader));
            // Zero workgroups exercise dispatch callbacks without running a shader whose resources are unbound.
            // Act: dispatch zero workgroups so the callback runs without executing an unbound shader.
            context.Dispatch(new(0));
            native.RequireSuccessfulObservation();
            // Assert: callback recursion is rejected and dispatch observes a live owner exactly once.
            Assert.Equal(1, calls);
            pipeline.Dispose();
#if DEBUG
            Assert.Throws<ObjectDisposedException>(() => context.Dispatch(new(0)));
#endif
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
        // Arrange: select a compute executable, then retire both its pipeline and shader names.
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        try
        {
            var context = RHICommandContext.Current;
            // Act: replace the deleted native program with another executable through a scoped boundary.
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
            // Assert: restoring the replacement scope does not rebind the deleted program name.
            Assert.Equal(OpenTK.Graphics.OpenGL.ErrorCode.NoError, OpenTK.Graphics.OpenGL.GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }
    #endregion

    #region Private
    /// <summary>Preserves the native workgroup-count ABI during guarded lifetime assertions.</summary>
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void DispatchCompute(uint x, uint y, uint z);
    #endregion
}
