using System;
using System.Runtime.InteropServices;

using VanillaGraphicsExpanded.LumOn.Scene;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.Unit.LumOn.Scene;

/// <summary>Guards CPU records used by the surface-cache shader storage contracts.</summary>
public sealed class LumonSceneGpuStructLayoutTests
{
    #region Public API
    /// <summary>Both capture and outgoing-light queries index the chunk-slot array as consecutive std430 ivec4 values.</summary>
    [Fact]
    public void ChunkSlotsMatchShaderIvec4ArrayStride()
    {
        // Arrange
        var slots = new LumonSceneChunkSlotInfoGpu[]
        {
            new(new(-32, 32, 0, 7)), new(new(0, 32, 0, 8))
        };
        Assert.Equal(16, Marshal.SizeOf<LumonSceneChunkSlotInfoGpu>());
        // Act and Assert
        var words = MemoryMarshal.Cast<LumonSceneChunkSlotInfoGpu, int>(slots.AsSpan());
        Assert.Equal(new[] { -32, 32, 0, 7, 0, 32, 0, 8 }, words.ToArray());
    }

    /// <summary>Verifies the patch metadata gpu size is stable and aligned behavior.</summary>
    [Fact]
    public void PatchMetadataGpu_Size_IsStableAndAligned()
    {
        // Arrange no instance data: this contract is the unmanaged record's declared layout.
        // Act and assert its byte size and 16-byte alignment.
        int size = Marshal.SizeOf<LumonScenePatchMetadataGpu>();
        Assert.Equal(96, size); // 6x16 bytes (Vector4*4 + 8 uints)
        Assert.Equal(0, size % 16);
    }

    /// <summary>Verifies the work queue items are16 bytes behavior.</summary>
    [Fact]
    public void WorkQueueItems_Are16Bytes()
    {
        // Arrange no instance data: all three GPU queue records share the same packed stride.
        // Act and assert each managed layout is exactly 16 bytes.
        Assert.Equal(16, Marshal.SizeOf<LumonScenePageRequestGpu>());
        Assert.Equal(16, Marshal.SizeOf<LumonSceneCaptureWorkGpu>());
        Assert.Equal(16, Marshal.SizeOf<LumonSceneRelightWorkGpu>());
    }
    #endregion
}











