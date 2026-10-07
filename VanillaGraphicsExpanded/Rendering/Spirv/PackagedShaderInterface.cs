using System.Collections.Immutable;
using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.Rendering.Spirv;

/// <summary>Portable compiler facts associated with one exact binary and structural selection.</summary>
internal sealed record PackagedShaderInterface(
    int Version,
    string Extractor,
    ShaderStageKind Stage,
    string EntryPoint,
    string StructuralKey,
    string Configuration,
    PackagedStageInterface Interface)
{
    public const int CurrentVersion = 2;
}

/// <summary>Portable numeric declarations for one structural shader variant.</summary>
internal sealed record PackagedStageInterface(
    ImmutableArray<PackagedInterfaceVariable> Inputs,
    ImmutableArray<PackagedInterfaceVariable> Outputs,
    ImmutableArray<PackagedExecutionMode> ExecutionModes);

/// <summary>A portable stage declaration, not proof of final linked activity or a driver address.</summary>
internal sealed record PackagedInterfaceVariable(
    uint Location,
    uint Index,
    uint Component,
    ShaderScalarType Scalar,
    uint BitWidth,
    uint VectorSize,
    uint Columns,
    ImmutableArray<uint> ArrayDimensions);

/// <summary>A compiler execution mode with explicit distinction between literal arguments and binary-local IDs.</summary>
internal sealed record PackagedExecutionMode(uint Mode, ImmutableArray<uint> Arguments, bool ArgumentsAreIds = false);
