using System;

namespace VanillaGraphicsExpanded.Rendering.RHI;

/// <summary>Returns a fully initialized owner or diagnostics without exposing provisional identity.</summary>
public readonly struct RHICreationResult<TResource> where TResource : class
{
    private readonly TResource? resource;

    #region Public API
    /// <summary>Reports whether a complete owned resource was produced.</summary>
    public bool IsSuccess => resource is not null;
    /// <summary>Gets detected creation failure diagnostics; default is an unsuccessful result.</summary>
    public string? Diagnostics { get; }
    /// <summary>Returns the initialized owner or throws without exposing an incomplete object.</summary>
    public TResource RequireResource() => resource ?? throw new InvalidOperationException(Diagnostics ?? "Resource creation did not complete.");
    /// <summary>Obtains ownership only when initialization completed.</summary>
    public bool TryGetResource(out TResource? value) { value = resource; return value is not null; }
    #endregion

    #region Internal API
    /// <summary>Constructs a mutually exclusive success or failure result.</summary>
    internal RHICreationResult(TResource? resource, string? diagnostics) { this.resource = resource; Diagnostics = diagnostics; }
    #endregion
}
