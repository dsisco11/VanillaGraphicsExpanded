using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace VanillaGraphicsExpanded.Rendering.RHI;

/// <summary>Owns frozen source or binary stage inputs, an entry point and typed specialization values.</summary>
public sealed class RHIShaderStageDescription
{
    #region Public API
    /// <summary>Gets the executable stage.</summary>
    public ShaderStage Stage { get; }
    /// <summary>Gets the explicitly supplied code representation.</summary>
    public ShaderCodeFormat Format { get; }
    /// <summary>Gets immutable GLSL text, when GLSL was supplied.</summary>
    public string? Source { get; }
    /// <summary>Gets the selected entry point.</summary>
    public string EntryPoint { get; }
    /// <summary>Gets copied binary bytes, when SPIR-V was supplied.</summary>
    public ImmutableArray<byte> Binary { get; }
    /// <summary>Gets copied specialization constants in deterministic identifier order.</summary>
    public ImmutableArray<RHIShaderSpecialization> Specializations { get; }
    /// <summary>Freezes preprocessed GLSL; native GLSL compilation requires its main entry point.</summary>
    public RHIShaderStageDescription(ShaderStage stage, string source)
    {
        if (!Enum.IsDefined(stage)) throw new ArgumentOutOfRangeException(nameof(stage));
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        Stage = stage; Source = source; EntryPoint = "main"; Format = ShaderCodeFormat.Glsl;
        Binary = []; Specializations = [];
    }
    /// <summary>Copies an explicitly selected SPIR-V module and rejects malformed or duplicate specialization inputs.</summary>
    public RHIShaderStageDescription(ShaderStage stage, ReadOnlySpan<byte> binary, string entryPoint = "main",
        IEnumerable<RHIShaderSpecialization>? specializations = null)
    {
        if (!Enum.IsDefined(stage)) throw new ArgumentOutOfRangeException(nameof(stage));
        ArgumentException.ThrowIfNullOrWhiteSpace(entryPoint);
        if (entryPoint.Contains('\0')) throw new ArgumentException("The entry point contains a null terminator.", nameof(entryPoint));
        // This is the SPIR-V file-format magic, not a native API enumeration value.
        if (binary.Length < 20 || binary.Length % sizeof(uint) != 0 || BinaryPrimitives.ReadUInt32LittleEndian(binary) != 0x07230203)
            throw new ArgumentException("Invalid SPIR-V binary header.", nameof(binary));
        SpirvEntryPointValidation.Require(binary, stage, entryPoint);
        Stage = stage; EntryPoint = entryPoint; Format = ShaderCodeFormat.Spirv;
        Binary = ImmutableArray.Create(binary.ToArray());
        Specializations = (specializations ?? []).OrderBy(value => value.Id).ToImmutableArray();
        if (Specializations.Select(value => value.Id).Distinct().Count() != Specializations.Length)
            throw new ArgumentException("Specialization identifiers must be unique.", nameof(specializations));
    }
    #endregion
}
