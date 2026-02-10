using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.Tests.GPU.Helpers;

internal static class LumonSceneCaptureVoxelParamsUbo
{
    public const int ParamsSizeBytes = 64;

    public static void Bind(
        ObjectParamsUbo paramsUbo,
        uint tileSizeTexels,
        uint tilesPerAxis,
        uint tilesPerAtlas,
        uint borderTexels,
        int occOriginMinCell0X,
        int occOriginMinCell0Y,
        int occOriginMinCell0Z,
        int occRing0X,
        int occRing0Y,
        int occRing0Z,
        int occResolution)
    {
        Span<byte> bytes = stackalloc byte[ParamsSizeBytes];

        // uvec4 atlasLayout
        UboPacking.WriteUVec4(bytes, byteOffset: 0, x: tileSizeTexels, y: tilesPerAxis, z: tilesPerAtlas, w: borderTexels);

        // ivec4 occOriginMinCell0 (xyz)
        UboPacking.WriteIVec4(bytes, byteOffset: 16, x: occOriginMinCell0X, y: occOriginMinCell0Y, z: occOriginMinCell0Z, w: 0);

        // ivec4 occRing0 (xyz)
        UboPacking.WriteIVec4(bytes, byteOffset: 32, x: occRing0X, y: occRing0Y, z: occRing0Z, w: 0);

        // ivec4 occInts0 (x = occResolution)
        UboPacking.WriteIVec4(bytes, byteOffset: 48, x: occResolution, y: 0, z: 0, w: 0);

        paramsUbo.UploadAndBind(bytes);
    }
}
