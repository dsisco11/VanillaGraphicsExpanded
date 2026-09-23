using System;

namespace VanillaGraphicsExpanded.Rendering.Contracts;

/// <summary>Defines a typed shader setting, its default and its validation metadata on a partial property.</summary>
/// <remarks>
/// An instance partial get/set property generates an immutable <c>&lt;PropertyName&gt;Option</c> key and
/// accessors that call the owner's <c>GetShaderOption</c> and <c>SetShaderOptions</c> hooks. Wrap related
/// property assignments in <c>ConfigureOptions</c> to publish and schedule them atomically. In GpuProgram,
/// those hooks validate through ShaderSettings and use the existing recompile scheduler. A static
/// get-only partial <see cref="ShaderOption{T}"/> property instead publishes a reusable option key.
/// Supported value types are bool, int, uint, float and top-level enums backed by int or uint.
/// Attribute constants must match that value type exactly; floats must be finite. Fields, auto-properties
/// and init-only setters are not supported. This declares the setting, not a stage dependency:
/// use <see cref="ShaderUseAttribute"/> to select structural or specialization use, and
/// <see cref="ShaderOptionReferenceAttribute"/> to reuse its definition on another property.
/// </remarks>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
internal sealed class ShaderOptionAttribute : Attribute
{
    #region Declaration
    /// <summary>Declares the canonical GLSL setting name and typed fallback used when no value is selected.</summary>
    /// <param name="name">Explicit canonical GLSL identifier, independent of the C# property name.</param>
    /// <param name="defaultValue">Constant matching the property's value type (or the static key's T), within its declared domain and inclusive bounds.</param>
    public ShaderOptionAttribute(string name, object defaultValue) { }

    /// <summary>Gets or sets the finite set of accepted typed values; null leaves the domain unspecified.</summary>
    /// <remarks>Boolean keys receive their natural false/true domain when omitted. Integer and enum structural uses require an explicit, nonempty, duplicate-free domain. Specializations can instead use bounds without a finite domain.</remarks>
    public object[]? Domain { get; set; }

    /// <summary>Gets or sets an inclusive typed lower bound; null supplies no additional lower limit.</summary>
    /// <remarks>The bound must match the option's value type. The default and every finite-domain value must satisfy it.</remarks>
    public object? Minimum { get; set; }

    /// <summary>Gets or sets an inclusive typed upper bound; null supplies no additional upper limit.</summary>
    /// <remarks>The bound must match the option's value type and must not be below Minimum. The default and every finite-domain value must satisfy it.</remarks>
    public object? Maximum { get; set; }

    /// <summary>Gets or sets compatibility names that resolve to this same canonical setting.</summary>
    /// <remarks>Names must be distinct valid GLSL identifiers. The settings model accepts equivalent alias/canonical inputs and rejects conflicting values; aliases do not create separate keys or variants.</remarks>
    public string[]? Aliases { get; set; }
    #endregion
}
