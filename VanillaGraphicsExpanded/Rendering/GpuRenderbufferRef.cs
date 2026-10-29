using System;
using System.Runtime.CompilerServices;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Retains renderbuffer consumption authority without exposing disposal, mapping or storage replacement.</summary>
public readonly struct GpuRenderbufferRef : IEquatable<GpuRenderbufferRef>
{
    private readonly GpuResource<GpuRenderbufferMetadata>? resource;

    #region Public API
    /// <summary>Reports absence independently of the retained allocation's lifetime.</summary>
    public bool IsEmpty => resource is null;
    /// <summary>Reports manager-certified availability without recovery or native queries.</summary>
    public bool IsValid => TryGetMetadata(out _);
    /// <summary>Returns already published facts only.</summary>
    public bool TryGetMetadata(out GpuRenderbufferMetadata data) => RHIResourceManager.TryGetRenderbufferReferenceMetadata(resource, out data);
    /// <summary>Compares managed identity independently of mutable native allocation.</summary>
    public bool Equals(GpuRenderbufferRef other) => ReferenceEquals(resource, other.resource);
    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is GpuRenderbufferRef other && Equals(other);
    /// <inheritdoc />
    public override int GetHashCode() => resource is null ? 0 : RuntimeHelpers.GetHashCode(resource);
    /// <summary>Compares retained wrappers.</summary>
    public static bool operator ==(GpuRenderbufferRef left, GpuRenderbufferRef right) => left.Equals(right);
    /// <summary>Reports distinct retained wrappers.</summary>
    public static bool operator !=(GpuRenderbufferRef left, GpuRenderbufferRef right) => !left.Equals(right);
    #endregion

    #region Internal API
    /// <summary>Retains an existing typed wrapper without registration or acquisition.</summary>
    internal GpuRenderbufferRef(GpuResource<GpuRenderbufferMetadata>? resource) => this.resource = resource;
    /// <summary>Supplies the resource only to manager/backend implementation boundaries.</summary>
    internal GpuResource<GpuRenderbufferMetadata> RequireResource() => resource ?? throw new InvalidOperationException("A renderbuffer is required.");
    #endregion
}
