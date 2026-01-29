using System;
using System.Numerics;

namespace VanillaGraphicsExpanded.LumOn.Scene;

internal readonly record struct LumonSceneMeshCardCaptureJob
{
    public readonly uint PhysicalPageId;
    public readonly uint ChunkSlot;
    public readonly uint PatchId;

    public readonly Vector3 OriginWS;
    public readonly Vector3 AxisUWS;
    public readonly Vector3 AxisVWS;
    public readonly Vector3 NormalWS;

    public readonly uint TriangleOffset;
    public readonly uint TriangleCount;

    public LumonSceneMeshCardCaptureJob(
        uint physicalPageId,
        uint chunkSlot,
        uint patchId,
        Vector3 originWS,
        Vector3 axisUWS,
        Vector3 axisVWS,
        Vector3 normalWS,
        uint triangleOffset,
        uint triangleCount)
    {
        PhysicalPageId = physicalPageId;
        ChunkSlot = chunkSlot;
        PatchId = patchId;

        OriginWS = originWS;
        AxisUWS = axisUWS;
        AxisVWS = axisVWS;
        NormalWS = normalWS;

        TriangleOffset = triangleOffset;
        TriangleCount = triangleCount;
    }

    public static LumonSceneMeshCardCaptureJob FromModelSpaceCard(
        uint physicalPageId,
        uint chunkSlot,
        uint patchId,
        in LumonSceneMeshCard card,
        in Matrix4x4 modelToWorld,
        uint triangleOffset,
        uint triangleCount)
    {
        if (physicalPageId == 0) throw new ArgumentOutOfRangeException(nameof(physicalPageId));

        card.TransformToWorld(modelToWorld, out Vector3 originWS, out Vector3 axisUWS, out Vector3 axisVWS, out Vector3 normalWS);
        return new LumonSceneMeshCardCaptureJob(
            physicalPageId,
            chunkSlot,
            patchId,
            originWS,
            axisUWS,
            axisVWS,
            normalWS,
            triangleOffset,
            triangleCount);
    }
}
