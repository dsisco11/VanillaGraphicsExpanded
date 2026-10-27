using VanillaGraphicsExpanded.Rendering.RHI;
using System;
using System.Runtime.CompilerServices;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Provides texture consumers with published facts without exposing ownership or mutation.</summary>
public readonly struct GpuTextureRef : IEquatable<GpuTextureRef>
{
    private readonly GpuResource<GpuTextureMetadata>? resource;

    #region Public API
    /// <summary>Reports absence of a managed resource, independently of its usability.</summary>
    public bool IsEmpty => resource is null;
    /// <summary>Reports manager-certified usability without acquiring native facts.</summary>
    public bool IsValid => TryGetMetadata(out _);
    /// <summary>Gets the current usable native name without transferring ownership.</summary>
    public int TextureId { get { RequireMetadata(); return (int)resource!.ResourceId; } }
    /// <summary>Gets the published texture target.</summary>
    public TextureDimension Target => RequireMetadata().Target;
    /// <summary>Returns already-published usable facts without queries or recovery.</summary>
    public bool TryGetMetadata(out GpuTextureMetadata data) =>
        GpuResourceManager.TryGetTextureReferenceMetadata(resource, out data);
    /// <summary>Compares retained managed objects independently of mutable native identity.</summary>
    public bool Equals(GpuTextureRef other) => ReferenceEquals(resource, other.resource);
    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is GpuTextureRef other && Equals(other);
    /// <inheritdoc />
    public override int GetHashCode() => resource is null ? 0 : RuntimeHelpers.GetHashCode(resource);
    /// <summary>Compares consumer references by managed identity.</summary>
    public static bool operator ==(GpuTextureRef left, GpuTextureRef right) => left.Equals(right);
    /// <summary>Reports distinct managed identities.</summary>
    public static bool operator !=(GpuTextureRef left, GpuTextureRef right) => !left.Equals(right);
    #endregion

    #region Internal API
    /// <summary>Retains a resource without registration, acquisition or native allocation.</summary>
    internal GpuTextureRef(GpuResource<GpuTextureMetadata>? resource) => this.resource = resource;
    /// <summary>Supplies the backing wrapper only to rendering implementation boundaries.</summary>
    internal GpuResource<GpuTextureMetadata>? Resource => resource;
    /// <summary>Requires usable published facts and the originating submission context.</summary>
    internal GpuTextureMetadata RequireMetadata() => GpuResourceManager.RequireTextureReferenceMetadata(resource);
    /// <summary>Selects an allocated view-relative image from published facts.</summary>
    internal GpuTextureLevelMetadata RequireImage(int level)
    {
        foreach (var image in RequireMetadata().Levels)
            if (image.Level == level && image.Allocated) return image;
        throw new InvalidOperationException("Texture mip image is not allocated.");
    }
    #endregion
}
