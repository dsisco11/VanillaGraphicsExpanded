using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Integration;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Tests.Support.GPU.Native;

namespace VanillaGraphicsExpanded.Tests.Component.Rendering.Shaders;

/// <summary>Checks deferred executable completion against an independently retired context generation.</summary>
[Collection("OperationContext")]
[Trait("Category", "GpuIntegration")]
public sealed class GpuProgramDeferredContextTests(HeadlessGLFixture fixture)
{
    #region Public API
    /// <summary>Completion after context retirement refuses driver access and cannot publish into a replacement context.</summary>
    [Fact]
    public void RetiredContextRejectsDeferredCompletionBeforeQueries()
    {
        // Arrange
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        var executionBackend = new VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLBackend(OpenGlStateCache.Current);
        int native = 0;
        try
        {
            using var owner = NativeResourceSetup.CreateProgram();
            native = owner.ProgramId;
            executionBackend.SubmitProgramExecutable(owner);
            var entry = RHIResourceManagerSystem.CurrentManager!.GetResourceRegistration(owner)!;
            using var queries = new ExecutableDescriptorCapture();
            RenderContextRegistry.Retire(fixture);
            fixture.RestoreContextRegistration();
            // Act
            Assert.Throws<InvalidOperationException>(() => executionBackend.CompleteProgramLink(owner, out _));
            // Assert
            Assert.Equal(0, queries.ProgramReads);
            Assert.Equal(GpuResourceRegistrationState.ContextRetired, entry.State);
            Assert.False(entry.MetadataAvailable);
        }
        finally
        {
            fixture.RestoreContextRegistration();
            if (native != 0 && GL.IsProgram(native)) VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.RetireResource(new(VanillaGraphicsExpanded.Rendering.GpuResourceKind.Program, native));
            fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll();
        }
    }
    #endregion
}
