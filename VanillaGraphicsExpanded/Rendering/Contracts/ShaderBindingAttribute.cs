using System;

namespace VanillaGraphicsExpanded.Rendering.Contracts;

/// <summary>Declares the layout index and stage applicability of a typed interface binding property.</summary>
/// <remarks>The attribute owns the index; the generated property supplies typed resource access.</remarks>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
internal sealed class ShaderBindingAttribute : Attribute
{
    #region Public API
    /// <summary>Declares a shader identifier, resource kind and applicable pipeline stages.</summary>
    public ShaderBindingAttribute(string name, ShaderBindingKind kind, int index, params ShaderStageKind[] stages) { }
    /// <summary>Selects a generated program member, or all programs on the consumer when omitted.</summary>
    public string? Program { get; set; }
    /// <summary>Selects several generated program members without duplicating one property declaration.</summary>
    public string[]? Programs { get; set; }
    /// <summary>Controls missing-resource diagnostics; optimized-away resources remain legal.</summary>
    public bool Required { get; set; } = true;
    /// <summary>Declares the texture target for raw IDs and unassigned optional samplers.</summary>
    public ShaderTextureTarget TextureTarget { get; set; } = ShaderTextureTarget.Texture2D;
    /// <summary>Selects a shared sampler policy: NearestClamp, LinearClamp or ShadowCompareLinearClamp.</summary>
    public ShaderSamplerPolicy Sampler { get; set; }
    /// <summary>Declares the occupied binding range for a resource array.</summary>
    public int ArrayLength { get; set; } = 1;
    /// <summary>Constrains the linked sampler or image type; Unspecified permits any type of the declared resource kind.</summary>
    public ShaderResourceType ShaderType { get; set; }
    #endregion
}
