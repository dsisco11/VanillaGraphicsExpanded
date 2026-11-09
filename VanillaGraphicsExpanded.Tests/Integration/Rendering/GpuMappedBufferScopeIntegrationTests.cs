using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using System;

using OpenTK.Graphics.OpenGL;

using VanillaGraphicsExpanded.Rendering;

using Xunit;

namespace VanillaGraphicsExpanded.Tests.Integration.Rendering;

/// <summary>
/// Integration tests for <see cref="GpuMappedBufferScope{T}"/> and the <see cref="GpuBufferObject.MapScope{T}"/> API.
/// </summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public class GpuMappedBufferScopeIntegrationTests
{
    private readonly HeadlessGLFixture fixture;

    #region Public API

    /// <summary>Uses the shared headless OpenGL fixture for native buffer operations.</summary>
    public GpuMappedBufferScopeIntegrationTests(HeadlessGLFixture fixture)
    {
        this.fixture = fixture;
    }

    /// <summary>Writes values through a mapped scope and reads the same data back from the buffer.</summary>
    [Fact]
    public void MapScope_WriteAndReadBack_RoundTripsData()
    {
        // Arrange
        fixture.MakeCurrent();

        const int count = 256;
        int byteCount = checked(count * sizeof(int));

        using var vbo = RHICommandContext.Current.CreateVertexBuffer(new RHIBufferDescription { Role = BufferRole.Vertex, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.DynamicDraw, Label = "MappedVbo" }).RequireResource();
        vbo.Allocate(byteCount);
        Assert.Equal(byteCount, GetActualSizeBytes(vbo));

        // Act: write through the mapped scope, whose disposal completes the write mapping.
        using (var mapped = vbo.MapScope<int>(0, count, BufferMapAccess.Write | BufferMapAccess.InvalidateBuffer))
        {
            Assert.True(mapped.IsMapped);
            Assert.Equal(count, mapped.Span.Length);

            for (int i = 0; i < mapped.Span.Length; i++)
            {
                mapped.Span[i] = i * 3;
            }
        }

        // Assert the values crossed the GPU buffer boundary and can be read back.
        int[] readback = new int[count];
        ReadBack(vbo, readback);

        for (int i = 0; i < readback.Length; i++)
        {
            Assert.Equal(i * 3, readback[i]);
        }
    }

    /// <summary>Flushes explicitly written mapped values and verifies them through a subsequent read.</summary>
    [Fact]
    public void MapScope_WithExplicitFlush_FlushesAndRoundTripsData()
    {
        // Arrange
        fixture.MakeCurrent();

        const int count = 128;
        int byteCount = checked(count * sizeof(int));

        using var vbo = RHICommandContext.Current.CreateVertexBuffer(new RHIBufferDescription { Role = BufferRole.Vertex, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.StreamDraw, Label = "FlushMappedVbo" }).RequireResource();
        vbo.Allocate(byteCount);
        Assert.Equal(byteCount, GetActualSizeBytes(vbo));

        // Act: explicitly flush the mapped range before closing the mapping.
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

        // Assert the flushed values are visible through the native buffer readback.
        int[] readback = new int[count];
        ReadBack(vbo, readback);

        for (int i = 0; i < readback.Length; i++)
        {
            Assert.Equal(0x12340000 + i, readback[i]);
        }
    }

    #endregion
    #region Private

    /// <summary>Copies a buffer's native contents into the supplied CPU array.</summary>
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

    /// <summary>Reads the allocated byte capacity of the bound native buffer.</summary>
    private static int GetActualSizeBytes(GpuVbo vbo)
    {
        using var bind = OpenGlStateCache.Current.BindBufferScope(VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLBufferValues.Target(vbo.Role), vbo.BufferId);
        GL.GetBufferParameter(VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLBufferValues.Target(vbo.Role), BufferParameterName.BufferSize, out int size);
        return size;
    }
    #endregion
}
