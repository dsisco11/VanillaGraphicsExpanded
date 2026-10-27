using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
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

        using var vbo = RHICommandContext.Current.CreateVertexBuffer(new RHIBufferDescription { Role = BufferRole.Vertex, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.DynamicDraw, Label = "MappedVbo" }).RequireResource();
        vbo.Allocate(byteCount);
        Assert.Equal(byteCount, GetActualSizeBytes(vbo));

        using (var mapped = vbo.MapScope<int>(0, count, BufferMapAccess.Write | BufferMapAccess.InvalidateBuffer))
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

        using var vbo = RHICommandContext.Current.CreateVertexBuffer(new RHIBufferDescription { Role = BufferRole.Vertex, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.StreamDraw, Label = "FlushMappedVbo" }).RequireResource();
        vbo.Allocate(byteCount);
        Assert.Equal(byteCount, GetActualSizeBytes(vbo));

        using (var mapped = vbo.MapScope<int>(0, count, BufferMapAccess.Write | BufferMapAccess.ExplicitFlush))
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

        var vbo = RHICommandContext.Current.CreateVertexBuffer(new RHIBufferDescription { Role = BufferRole.Vertex, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.StaticDraw, Label = "DisposedMappedVbo" }).RequireResource();
        vbo.Dispose();

        bool result = vbo.TryMapScope<int>(0, 1, BufferMapAccess.Write, out GpuMappedBufferScope<int> scope);

        Assert.False(result);
        Assert.False(scope.IsMapped);
    }

    private static unsafe void ReadBack(GpuVbo vbo, int[] destination)
    {
        using var bind = OpenGlStateCache.Current.BindBufferScope(VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLBufferValues.Target(vbo.Role), vbo.BufferId);

        fixed (int* ptr = destination)
        {
            GL.GetBufferSubData(
                VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLBufferValues.Target(vbo.Role),
                IntPtr.Zero,
                (IntPtr)checked(destination.Length * sizeof(int)),
                (IntPtr)ptr);
        }
    }

    private static int GetActualSizeBytes(GpuVbo vbo)
    {
        using var bind = OpenGlStateCache.Current.BindBufferScope(VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLBufferValues.Target(vbo.Role), vbo.BufferId);
        GL.GetBufferParameter(VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLBufferValues.Target(vbo.Role), BufferParameterName.BufferSize, out int size);
        return size;
    }
}
