using System;

using OpenTK.Graphics.OpenGL;

using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

using Xunit;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>
/// Integration tests for <see cref="GpuMappedBufferScope{T}"/> and the <see cref="GpuBufferObject.MapScope{T}"/> API.
/// </summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public class GpuMappedBufferScopeIntegrationTests
{
    private readonly HeadlessGLFixture fixture;

    public GpuMappedBufferScopeIntegrationTests(HeadlessGLFixture fixture)
    {
        this.fixture = fixture;
    }

    [Fact]
    public void MapScope_WriteAndReadBack_RoundTripsData()
    {
        fixture.MakeCurrent();

        const int count = 256;
        int byteCount = checked(count * sizeof(int));

        using var vbo = GpuVbo.Create(usage: BufferUsageHint.DynamicDraw, debugName: "MappedVbo");
        vbo.Allocate(byteCount);
        Assert.Equal(byteCount, GetActualSizeBytes(vbo));

        using (var mapped = vbo.MapScope<int>(0, count, MapBufferAccessMask.MapWriteBit | MapBufferAccessMask.MapInvalidateBufferBit))
        {
            Assert.True(mapped.IsMapped);
            Assert.Equal(count, mapped.Span.Length);

            for (int i = 0; i < mapped.Span.Length; i++)
            {
                mapped.Span[i] = i * 3;
            }
        }

        int[] readback = new int[count];
        ReadBack(vbo, readback);

        for (int i = 0; i < readback.Length; i++)
        {
            Assert.Equal(i * 3, readback[i]);
        }
    }

    [Fact]
    public void MapScope_WithExplicitFlush_FlushesAndRoundTripsData()
    {
        fixture.MakeCurrent();

        const int count = 128;
        int byteCount = checked(count * sizeof(int));

        using var vbo = GpuVbo.Create(usage: BufferUsageHint.StreamDraw, debugName: "FlushMappedVbo");
        vbo.Allocate(byteCount);
        Assert.Equal(byteCount, GetActualSizeBytes(vbo));

        using (var mapped = vbo.MapScope<int>(0, count, MapBufferAccessMask.MapWriteBit | MapBufferAccessMask.MapFlushExplicitBit))
        {
            Assert.True(mapped.IsMapped);

            for (int i = 0; i < mapped.Span.Length; i++)
            {
                mapped.Span[i] = 0x12340000 + i;
            }

            mapped.Flush();
            mapped.Flush(0, byteCount);
        }

        int[] readback = new int[count];
        ReadBack(vbo, readback);

        for (int i = 0; i < readback.Length; i++)
        {
            Assert.Equal(0x12340000 + i, readback[i]);
        }
    }

    [Fact]
    public void TryMapScope_DisposedBuffer_ReturnsFalse()
    {
        fixture.MakeCurrent();

        var vbo = GpuVbo.Create(debugName: "DisposedMappedVbo");
        vbo.Dispose();

        bool result = vbo.TryMapScope<int>(0, 1, MapBufferAccessMask.MapWriteBit, out GpuMappedBufferScope<int> scope);

        Assert.False(result);
        Assert.False(scope.IsMapped);
    }

    private static unsafe void ReadBack(GpuVbo vbo, int[] destination)
    {
        using var bind = vbo.BindScope();

        fixed (int* ptr = destination)
        {
            GL.GetBufferSubData(
                vbo.Target,
                IntPtr.Zero,
                (IntPtr)checked(destination.Length * sizeof(int)),
                (IntPtr)ptr);
        }
    }

    private static int GetActualSizeBytes(GpuVbo vbo)
    {
        using var bind = vbo.BindScope();
        GL.GetBufferParameter(vbo.Target, BufferParameterName.BufferSize, out int size);
        return size;
    }
}
