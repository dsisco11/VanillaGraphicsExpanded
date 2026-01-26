using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.Cache.Artifacts;

internal enum ArtifactGpuTextureUploadKind
{
    Float2DFull = 0,
    Float2DRegion = 1,
    UShort2DFull = 2,
    Float3DRegion = 3,
}

internal readonly record struct ArtifactGpuTextureUploadPayload(
    GpuTexture Texture,
    ArtifactGpuTextureUploadKind Kind,
    float[]? FloatData = null,
    ushort[]? UShortData = null,
    int X = 0,
    int Y = 0,
    int Z = 0,
    int Width = 0,
    int Height = 0,
    int Depth = 0,
    int Priority = 0,
    int MipLevel = 0);
