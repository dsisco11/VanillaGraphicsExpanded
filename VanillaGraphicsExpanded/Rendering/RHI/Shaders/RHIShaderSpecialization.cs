using System;

namespace VanillaGraphicsExpanded.Rendering.RHI;

/// <summary>Freezes an explicitly typed, 32-bit specialization value and its constant identifier.</summary>
public readonly record struct RHIShaderSpecialization
{
    #region Public API
    /// <summary>Gets the declared constant identifier.</summary>
    public uint Id { get; }
    /// <summary>Gets the scalar representation of the supplied value.</summary>
    public ShaderSpecializationType Type { get; }
    /// <summary>Encodes a Boolean without numeric conversion through floating point.</summary>
    public RHIShaderSpecialization(uint id, bool value) : this(id, ShaderSpecializationType.Boolean, value ? 1u : 0u) { }
    /// <summary>Preserves the signed integer's exact 32-bit representation.</summary>
    public RHIShaderSpecialization(uint id, int value) : this(id, ShaderSpecializationType.Int32, unchecked((uint)value)) { }
    /// <summary>Preserves the unsigned integer's exact 32-bit representation.</summary>
    public RHIShaderSpecialization(uint id, uint value) : this(id, ShaderSpecializationType.UInt32, value) { }
    /// <summary>Preserves a finite IEEE single-precision value.</summary>
    public RHIShaderSpecialization(uint id, float value) : this(id, ShaderSpecializationType.Float32,
        float.IsFinite(value) ? BitConverter.SingleToUInt32Bits(value) : throw new ArgumentOutOfRangeException(nameof(value))) { }
    #endregion

    #region Internal API
    /// <summary>Gets immutable specialization bits for backend encoding.</summary>
    internal uint Bits { get; }
    #endregion

    #region Private
    /// <summary>Captures the representation selected by a typed constructor.</summary>
    private RHIShaderSpecialization(uint id, ShaderSpecializationType type, uint bits) { Id = id; Type = type; Bits = bits; }
    #endregion
}
