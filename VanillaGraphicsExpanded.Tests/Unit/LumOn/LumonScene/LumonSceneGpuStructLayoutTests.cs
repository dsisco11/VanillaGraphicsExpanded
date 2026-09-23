using System.Runtime.InteropServices;

using VanillaGraphicsExpanded.LumOn.Scene;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.Unit.LumOn.LumonScene;

/// <summary>Guards CPU records used by the surface-cache shader storage contracts.</summary>
public sealed class LumonSceneGpuStructLayoutTests
{
    /// <summary>Both capture and outgoing-light queries index the chunk-slot array as consecutive std430 ivec4 values.</summary>
    [Fact]
    public void ChunkSlotsMatchShaderIvec4ArrayStride()
    {
        var slots = new LumonSceneChunkSlotInfoGpu[]
        {
            new(new(-32, 32, 0, 7)), new(new(0, 32, 0, 8))
        };
        Assert.Equal(16, Marshal.SizeOf<LumonSceneChunkSlotInfoGpu>());
        var words = MemoryMarshal.Cast<LumonSceneChunkSlotInfoGpu, int>(slots.AsSpan());
        Assert.Equal(new[] { -32, 32, 0, 7, 0, 32, 0, 8 }, words.ToArray());
    }

    [Fact]
    public void PatchMetadataGpu_Size_IsStableAndAligned()
    {
        int size = Marshal.SizeOf<LumonScenePatchMetadataGpu>();
        Assert.Equal(96, size); // 6x16 bytes (Vector4*4 + 8 uints)
        Assert.Equal(0, size % 16);
    }

    [Fact]
    public void WorkQueueItems_Are16Bytes()
    {
        Assert.Equal(16, Marshal.SizeOf<LumonScenePageRequestGpu>());
        Assert.Equal(16, Marshal.SizeOf<LumonSceneCaptureWorkGpu>());
        Assert.Equal(16, Marshal.SizeOf<LumonSceneRelightWorkGpu>());
    }
}
