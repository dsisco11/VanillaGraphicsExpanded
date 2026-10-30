using VanillaGraphicsExpanded.Rendering.RHI;
using System.Collections.Immutable;


namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Contains native executable and interface facts; authored contracts and prepared layouts remain separate.</summary>
public readonly record struct GpuProgramMetadata(bool ExecutableAvailable,
    ImmutableArray<GpuProgramResourceMetadata> Resources, ImmutableArray<GpuProgramTextureUnitMetadata> TextureUnits,
    PrimitiveTopology? GeometryInput = null, PrimitiveTopology? TessellationOutput = null,
    (int X, int Y, int Z)? ComputeLocalSize = null){
    #region Public API
    /// <summary>Compares executable facts and interface values independently of collection allocation.</summary>
    public bool Equals(GpuProgramMetadata other) => ExecutableAvailable == other.ExecutableAvailable
        && GeometryInput == other.GeometryInput && TessellationOutput == other.TessellationOutput && ComputeLocalSize == other.ComputeLocalSize
        && MetadataCollections.Equal(Resources, other.Resources) && MetadataCollections.Equal(TextureUnits, other.TextureUnits);
    /// <summary>Hashes scalar executable facts and unordered interface values.</summary>
    public override int GetHashCode() => System.HashCode.Combine(ExecutableAvailable, GeometryInput, TessellationOutput,
        ComputeLocalSize, MetadataCollections.Hash(Resources), MetadataCollections.Hash(TextureUnits));
    #endregion
}

/// <summary>Contains typed immutable facts for one executable resource; null fields are unavailable or inapplicable.</summary>
public readonly record struct GpuProgramResourceMetadata(
    ProgramResourceKind Interface, int Index, string? Name,
    ShaderValueType? Type = null, int? Location = null, int? ArraySize = null,
    int? BlockIndex = null, int? BufferBinding = null, int? BufferDataSize = null,
    ShaderStageMask? ReferencedStages = null, int? LocationIndex = null);

/// <summary>Records a sampler or image unit assignment used by native interface preparation.</summary>
public readonly record struct GpuProgramTextureUnitMetadata(int Location, int Unit);
