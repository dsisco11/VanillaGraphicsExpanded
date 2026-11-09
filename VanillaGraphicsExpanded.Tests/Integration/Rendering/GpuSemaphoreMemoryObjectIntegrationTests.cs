using VanillaGraphicsExpanded.Rendering.RHI;
using OpenTK.Graphics.OpenGL;

using VanillaGraphicsExpanded.Rendering;

using Xunit;

namespace VanillaGraphicsExpanded.Tests.Integration.Rendering;

/// <summary>
/// Integration tests for <see cref="GpuSemaphore"/> and <see cref="GpuMemoryObject"/>.
/// </summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public class GpuSemaphoreMemoryObjectIntegrationTests
{
    private readonly HeadlessGLFixture fixture;

    /// <summary>Uses the shared native context for supported extension creation checks.</summary>
    public GpuSemaphoreMemoryObjectIntegrationTests(HeadlessGLFixture fixture)
    {
        this.fixture = fixture;
    }

    /// <summary>Creation and disposal do not require an imported semaphore payload.</summary>
    [Fact]
    public void Semaphore_Create_Dispose()
    {
        // Arrange: Make the shared context current and require the external-semaphore extension before creating an object.
        fixture.MakeCurrent();

        Assert.SkipWhen(!RHICommandContext.Current.Capabilities.ExternalSemaphores, "GL_EXT_semaphore not supported by this context.");

        // Act: Create and dispose an unimported semaphore; signaling is outside this creation contract.
        using var sem = RHICommandContext.Current.CreateSemaphore(new()).RequireResource();
        // Assert: The new native object is valid before its owner retires it.
        Assert.True(sem.IsValid);
        Assert.True(sem.SemaphoreId > 0);

        // Signal and wait require an imported payload; creation alone does not establish one.
        sem.Dispose();
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>An unimported memory object accepts its creation-time dedicated-memory policy.</summary>
    [Fact]
    public void MemoryObject_Create_Dispose()
    {
        // Arrange: Make the shared context current and require external-memory support for the requested policy.
        fixture.MakeCurrent();

        Assert.SkipWhen(!RHICommandContext.Current.Capabilities.ExternalMemory, "GL_EXT_memory_object not supported by this context.");

        // Act: Create and dispose an unimported memory object with the non-dedicated creation policy.
        using var mem = RHICommandContext.Current.CreateMemoryObject(new(Dedicated: false)).RequireResource();
        // Assert: The native object is valid before owner disposal and raises no GL error.
        Assert.True(mem.IsValid);
        Assert.True(mem.MemoryObjectId > 0);

        // Smoke test: ensure the entrypoints are usable without throwing.

        mem.Dispose();
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }
}
