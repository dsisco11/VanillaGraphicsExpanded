using System;

namespace VanillaGraphicsExpanded.Rendering.RHI;

/// <summary>Retains one immutable executable generation without exposing lifecycle or native configuration.</summary>
public readonly struct RHIShaderRef : IEquatable<RHIShaderRef>
{
    private readonly RHIShader? shader;
    #region Public API
    /// <summary>Reports absence independently of retirement.</summary>
    public bool IsEmpty => shader is null;
    /// <summary>Compares managed generation identity, not native names or matching descriptions.</summary>
    public bool Equals(RHIShaderRef other) => ReferenceEquals(shader, other.shader);
    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is RHIShaderRef other && Equals(other);
    /// <inheritdoc />
    public override int GetHashCode() => shader is null ? 0 : System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(shader);
    /// <summary>Compares exact executable generation references.</summary>
    public static bool operator ==(RHIShaderRef left, RHIShaderRef right) => left.Equals(right);
    /// <summary>Reports distinct executable generation references.</summary>
    public static bool operator !=(RHIShaderRef left, RHIShaderRef right) => !left.Equals(right);
    #endregion

    #region Internal API
    /// <summary>Captures the supplied owner without querying, registering or transferring lifecycle authority.</summary>
    internal RHIShaderRef(RHIShader? shader) { this.shader = shader; }
    /// <summary>Reads frozen stage inputs without acquiring native context or validating mutable resource state.</summary>
    internal RHIShaderDescription Describe()
    {
        var value = shader ?? throw new InvalidOperationException("A shader reference is required.");
        return value.Description;
    }
    /// <summary>Requires the originally retained generation before backend use.</summary>
    internal RHIShader RequireShader()
    {
        var value = shader ?? throw new InvalidOperationException("A shader reference is required.");
        value.RequireUsable();
        return value;
    }
    #endregion
}
