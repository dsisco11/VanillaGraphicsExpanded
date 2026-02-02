using VanillaGraphicsExpanded.LumOn.Scene;
using VanillaGraphicsExpanded.Numerics;
using VanillaGraphicsExpanded.Voxels.ChunkProcessing;

using Xunit;

namespace VanillaGraphicsExpanded.Tests.Unit.LumOn.Scene;

public sealed class LumonSceneOccupancyClipmapUpdateRendererDispatchPolicyTests
{
    [Fact]
    public void TryGetDispatchPayload_WhenSuccessWithArtifact_ReturnsTrueAndPayload()
    {
        ChunkKey key = ChunkKey.FromChunkCoords(0, 0, 0);
        uint[] payload = new uint[LumonSceneTraceSceneRegionUploadGpuResources.RegionCellCount];
        payload[0] = 123u;

        var artifact = new LumonSceneTraceSceneRegionArtifact(key, version: 1, regionCoord: VectorInt3.Zero, payloadWords: payload);

        var res = new ChunkWorkResult<LumonSceneTraceSceneRegionArtifact>(
            Status: ChunkWorkStatus.Success,
            Key: key,
            RequestedVersion: 1,
            ProcessorId: "test",
            Artifact: artifact,
            Error: ChunkWorkError.None,
            Reason: null);

        Assert.True(LumonSceneOccupancyClipmapUpdateRenderer.TryGetDispatchPayload(res, out var payloadWords));
        Assert.Equal(payload.Length, payloadWords.Length);
        Assert.Equal(123u, payloadWords.Span[0]);
    }

    [Fact]
    public void TryGetDispatchPayload_WhenNotSuccess_ReturnsFalse()
    {
        ChunkKey key = ChunkKey.FromChunkCoords(0, 0, 0);

        var res = new ChunkWorkResult<LumonSceneTraceSceneRegionArtifact>(
            Status: ChunkWorkStatus.ChunkUnavailable,
            Key: key,
            RequestedVersion: 1,
            ProcessorId: "test",
            Artifact: null,
            Error: ChunkWorkError.None,
            Reason: "ChunkUnavailable");

        Assert.False(LumonSceneOccupancyClipmapUpdateRenderer.TryGetDispatchPayload(res, out _));
    }
}
