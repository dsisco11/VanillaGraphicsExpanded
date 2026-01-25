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

    public GpuSemaphoreMemoryObjectIntegrationTests(HeadlessGLFixture fixture)
    {
        this.fixture = fixture;
    }

    [Fact]
    public void Semaphore_Create_Dispose()
    {
        fixture.MakeCurrent();

        Assert.SkipWhen(!GpuSemaphore.IsSupported, "GL_EXT_semaphore not supported by this context.");

        using var sem = GpuSemaphore.Create();
        Assert.True(sem.IsValid);
        Assert.True(sem.SemaphoreId > 0);

        // Some drivers may not report the object as "created" until first use.
        // This is a smoke test: ensure the entrypoints are usable without throwing.
        sem.Signal(ReadOnlySpan<int>.Empty, ReadOnlySpan<int>.Empty, ReadOnlySpan<TextureLayout>.Empty);
        sem.Wait(ReadOnlySpan<int>.Empty, ReadOnlySpan<int>.Empty, ReadOnlySpan<TextureLayout>.Empty);
    }

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
    }
}
