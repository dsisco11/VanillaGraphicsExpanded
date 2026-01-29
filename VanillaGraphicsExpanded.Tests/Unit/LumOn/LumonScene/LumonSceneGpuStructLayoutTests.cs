using System.Runtime.InteropServices;

using VanillaGraphicsExpanded.LumOn.Scene;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.Unit.LumOn.LumonScene;

public sealed class LumonSceneGpuStructLayoutTests
{
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

