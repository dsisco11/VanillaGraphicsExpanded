using VanillaGraphicsExpanded.Rendering.RHI;
using System.Collections.Immutable;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Contains native executable and interface facts; authored contracts and prepared layouts remain separate.</summary>
public readonly record struct GpuProgramMetadata(bool ExecutableAvailable,
    ImmutableArray<GpuProgramResourceMetadata> Resources, ImmutableArray<GpuProgramTextureUnitMetadata> TextureUnits,
    PrimitiveTopology? GeometryInput = null, PrimitiveTopology? TessellationOutput = null,
    (int X, int Y, int Z)? ComputeLocalSize = null);

/// <summary>Contains immutable query results for one indexed native program interface resource.</summary>
/// <param name="Name">The native resource name when present; authored contract names are stored separately.</param>
public readonly record struct GpuProgramResourceMetadata(
    ProgramInterface Interface, int Index, string? Name,
    ImmutableDictionary<ProgramProperty, ImmutableArray<int>> Properties);

/// <summary>Records a sampler or image unit assignment used by native interface preparation.</summary>
public readonly record struct GpuProgramTextureUnitMetadata(int Location, int Unit);
