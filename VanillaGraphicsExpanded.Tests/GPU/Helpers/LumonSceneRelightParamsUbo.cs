using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.Tests.GPU.Helpers;

internal static class LumonSceneRelightParamsUbo
{
    public const int ParamsSizeBytes = 80;

    public static void Bind(
        ObjectParamsUbo paramsUbo,
        uint tileSizeTexels,
        uint tilesPerAxis,
        uint tilesPerAtlas,
        uint borderTexels,
        uint texelsPerPagePerFrame,
        uint raysPerTexel,
        uint maxDdaSteps,
        uint debugCountersEnabled,
        int frameIndex,
        int occResolution,
        int occOriginMinCell0X,
        int occOriginMinCell0Y,
        int occOriginMinCell0Z,
        int occRing0X,
        int occRing0Y,
        int occRing0Z)
    {
        Span<byte> bytes = stackalloc byte[ParamsSizeBytes];

        // uvec4 atlasLayout
        UboPacking.WriteUVec4(bytes, byteOffset: 0, x: tileSizeTexels, y: tilesPerAxis, z: tilesPerAtlas, w: borderTexels);

        // uvec4 relightUints0
        UboPacking.WriteUVec4(bytes, byteOffset: 16, x: texelsPerPagePerFrame, y: raysPerTexel, z: maxDdaSteps, w: debugCountersEnabled);

        // ivec4 relightInts0
        UboPacking.WriteIVec4(bytes, byteOffset: 32, x: frameIndex, y: occResolution, z: 0, w: 0);

        // ivec4 occOriginMinCell0
        UboPacking.WriteIVec4(bytes, byteOffset: 48, x: occOriginMinCell0X, y: occOriginMinCell0Y, z: occOriginMinCell0Z, w: 0);

        // ivec4 occRing0
        UboPacking.WriteIVec4(bytes, byteOffset: 64, x: occRing0X, y: occRing0Y, z: occRing0Z, w: 0);

        paramsUbo.UploadAndBind(bytes);
    }
}
