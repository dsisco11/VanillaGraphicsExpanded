using System;

namespace VanillaGraphicsExpanded.Rendering.Contracts;

/// <summary>Reuses an attributed static option key without redeclaring its name, default, aliases or bounds.</summary>
/// <remarks>
/// Apply instead of <see cref="ShaderOptionAttribute"/> to an instance partial get/set property or a
/// static get-only partial ShaderOption&lt;T&gt; property. The referenced property must be a static key
/// declared through ShaderOption or another ShaderOptionReference, with the same value type.
/// The generator follows references, rejects cycles and emits a reference to the existing key rather
/// than copying its definition. Instance accessors use the same settings hooks as locally defined options.
/// Add <see cref="ShaderUseAttribute"/> on the consuming class to declare where the option affects a stage.
/// </remarks>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
internal sealed class ShaderOptionReferenceAttribute : Attribute
{
    #region Declaration
    /// <summary>Identifies an existing attributed static key to share with this property.</summary>
    /// <param name="owner">Partial declaration class containing the shared static ShaderOption&lt;T&gt; property.</param>
    /// <param name="member">Shared property name, normally supplied with <c>nameof(Owner.Option)</c>; not the canonical GLSL name.</param>
    public ShaderOptionReferenceAttribute(Type owner, string member) { }
    #endregion
}
