using OpenTK.Graphics.OpenGL;

using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

using Xunit;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>
/// Integration tests for <see cref="GpuSemaphore"/> and <see cref="GpuMemoryObject"/>.
/// </summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
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
        fixture.MakeCurrent();

        Assert.SkipWhen(!GpuSemaphore.IsSupported, "GL_EXT_semaphore not supported by this context.");

        using var sem = GpuSemaphore.Create();
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
        fixture.MakeCurrent();

        Assert.SkipWhen(!GpuMemoryObject.IsSupported, "GL_EXT_memory_object not supported by this context.");

        using var mem = GpuMemoryObject.Create();
        Assert.True(mem.IsValid);
        Assert.True(mem.MemoryObjectId > 0);

        // Smoke test: ensure the entrypoints are usable without throwing.
        mem.SetParameter(MemoryObjectParameterName.DedicatedMemoryObjectExt, 0);
        mem.Dispose();
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }
}
