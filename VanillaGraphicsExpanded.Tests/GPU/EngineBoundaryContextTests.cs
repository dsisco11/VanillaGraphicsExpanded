using System.Collections;
using System.Reflection;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Integration;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Verifies context rejection precedes every cleanup and native snapshot mutation.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class EngineBoundaryContextTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Public API
    /// <summary>A generation switch retains retryable cleanup and preserves operation failures without issuing restoration.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ContextGenerationMismatchRejectsBeforeCleanup(bool operationFailure)
    {
        EnsureContextValid();
        using var programs = new ComponentShaderPrograms();
        var sentinel = programs.Create<GpuProgramUseScopeTests.CountingShader>();
        var cache = StateCache.Current;
        cache.SetCapability(EnableCap.DepthTest, true);
        Assert.True(cache.TryBeginEngineBoundary(new EngineBoundaryDeclaration("ContextBoundary",
            new PipelineStateCoverage(depth: DepthStateKnowledge.TestEnabled)), out var boundary));
        var observer = new CleanupObserver();
        boundary!.AddCleanup(EngineBoundaryCleanup.Shader, observer);
        var original = RenderContextRegistry.Current();
        var registrations = (IDictionary)typeof(RenderContextRegistry).GetField("registrations", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        object registration = registrations[original.Handle]!;
        var replacement = new object();
        cache.SetCapability(EnableCap.DepthTest, false);
        GL.UseProgram(sentinel.ProgramId);
        long calls = cache.FixedFunctionCalls;
        try
        {
            if (operationFailure)
            {
                var expected = new InvalidOperationException("operation");
                var combined = Assert.Throws<AggregateException>(() => boundary.Run(() =>
                {
                    RenderContextRegistry.Retire(fixture);
                    RenderContextRegistry.RegisterCurrent(replacement, static _ => true);
                    throw expected;
                }));
                Assert.Contains(expected, combined.InnerExceptions);
                Assert.Contains(combined.InnerExceptions, failure => failure is EngineBoundaryRestoreException);
            }
            else
            {
                RenderContextRegistry.Retire(fixture);
                RenderContextRegistry.RegisterCurrent(replacement, static _ => true);
            }
            bool executed = false;
            Assert.Throws<EngineBoundaryRestoreException>(() => boundary.Run(() => executed = true));
            Assert.False(executed);
            Assert.Throws<EngineBoundaryRestoreException>(boundary.Dispose);
            Assert.Equal(0, observer.Calls);
            Assert.False(GL.IsEnabled(EnableCap.DepthTest));
            Assert.Equal(sentinel.ProgramId, GL.GetInteger(GetPName.CurrentProgram));
            Assert.Equal(calls, cache.FixedFunctionCalls);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally
        {
            // Restore the exact live registration receipt so the rejected boundary can be retried normally.
            lock (registrations) registrations[original.Handle] = registration;
            boundary.Dispose();
            Assert.True(GL.IsEnabled(EnableCap.DepthTest));
            StateCache.Current.UnbindProgram();
            cache.SetCapability(EnableCap.DepthTest, false);
        }
        Assert.Equal(1, observer.Calls);
        boundary.Dispose();
        Assert.Equal(1, observer.Calls);
    }
    #endregion

    #region Private
    /// <summary>Observes exactly-once cleanup without touching native drawing state.</summary>
    private sealed class CleanupObserver : IDisposable
    {
        #region Public API
        /// <summary>Counts cleanup attempts.</summary>
        public int Calls { get; private set; }
        /// <summary>Records one cleanup attempt.</summary>
        public void Dispose() => Calls++;
        #endregion
    }
    #endregion
}
