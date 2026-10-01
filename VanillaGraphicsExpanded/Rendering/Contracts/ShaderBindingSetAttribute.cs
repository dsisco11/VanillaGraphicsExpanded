using System;

namespace VanillaGraphicsExpanded.Rendering.Contracts;

/// <summary>Imports one compile-time shared property layout without runtime reflection.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Interface, AllowMultiple = true, Inherited = false)]
internal sealed class ShaderBindingSetAttribute : Attribute
{
    #region Public API
    /// <summary>References an interface whose attributed properties supply shared bindings.</summary>
    public ShaderBindingSetAttribute(Type owner) { }
    /// <summary>Selects a generated program member, or all programs when omitted.</summary>
    public string? Program { get; set; }
    /// <summary>Supplies include defaults only where explicit declarations have not supplied a shader name.</summary>
    public bool Defaults { get; set; }
    /// <summary>Restricts the imported layout to these stages; omission retains the properties' own stage applicability.</summary>
    public ShaderStageKind[]? Stages { get; set; }
    #endregion
}
