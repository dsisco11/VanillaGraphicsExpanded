using System;
using System.Runtime.CompilerServices;

namespace VanillaGraphicsExpanded.Rendering.RHI;

/// <summary>Retains an explicitly published external framebuffer surface without exposing native names or disposal.</summary>
public readonly struct RHISurfaceRef : IEquatable<RHISurfaceRef>
{
    private readonly GpuFramebuffer? surface;

    #region Public API
    /// <summary>Reports absence without inspecting mutable external surface metadata.</summary>
    public bool IsEmpty => surface is null;
    /// <summary>Compares the original integration recipient without using a numeric framebuffer name.</summary>
    public bool Equals(RHISurfaceRef other) => ReferenceEquals(surface, other.surface);
    /// <inheritdoc />
    public override bool Equals(object? value) => value is RHISurfaceRef other && Equals(other);
    /// <inheritdoc />
    public override int GetHashCode() => surface is null ? 0 : RuntimeHelpers.GetHashCode(surface);
    /// <summary>Compares integration identities independently of native name reuse.</summary>
    public static bool operator ==(RHISurfaceRef left, RHISurfaceRef right) => left.Equals(right);
    /// <summary>Reports distinct integration identities.</summary>
    public static bool operator !=(RHISurfaceRef left, RHISurfaceRef right) => !left.Equals(right);
    #endregion

    #region Internal API
    /// <summary>Accepts only a nonowning integration recipient; attachment-owned targets use normal realization.</summary>
    internal RHISurfaceRef(GpuFramebuffer surface)
    {
        ArgumentNullException.ThrowIfNull(surface);
        if (surface.OwnsNativeFramebuffer) throw new ArgumentException("An explicit surface requires externally owned framebuffer storage.", nameof(surface));
        this.surface = surface;
    }
    /// <summary>Supplies the original integration recipient solely to target realization and pass validation.</summary>
    internal GpuFramebuffer RequireSurface() => surface ?? throw new InvalidOperationException("An explicit surface is required.");
    #endregion
}
