using VanillaGraphicsExpanded.Rendering.RHI;
using System.Collections.Immutable;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Describes native texture storage and associations independently of sampler policy or contents.</summary>
public readonly record struct GpuTextureMetadata(
    TextureDimension Target,
    ImmutableArray<GpuTextureLevelMetadata> Levels,
    bool ImmutableStorage,
    int ImmutableLevels,
    int BaseLevel,
    int MaxLevel,
    GpuTextureBufferMetadata? Buffer = null,
    GpuTextureViewMetadata? View = null,
    nint Renderbuffer = 0);

/// <summary>Describes one native mip image or cube face; unallocated images remain explicit.</summary>
public readonly record struct GpuTextureLevelMetadata(
    int Level, CubeFace Face, int Width, int Height, int Depth,
    TextureFormat Format, int Samples, bool Allocated);

/// <summary>Describes the buffer associated with a texture, retaining native-width offsets and ranges.</summary>
public readonly record struct GpuTextureBufferMetadata(
    nint BufferId, TextureFormat Format, nint Offset, nint Size, bool WholeBuffer);

/// <summary>Describes a native view range without inventing an externally unqueryable source name.</summary>
public readonly record struct GpuTextureViewMetadata(int BaseLevel, int LevelCount, int BaseLayer, int LayerCount);
