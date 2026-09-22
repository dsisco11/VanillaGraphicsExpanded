using System;

namespace VanillaGraphicsExpanded.Rendering.Contracts;

/// <summary>Declares a shader program and generates its immutable static contract on the attributed class.</summary>
/// <remarks>
/// Apply to a top-level, nongeneric partial class. The source generator reads the attribute arguments
/// at compile time and emits a <see cref="GpuShaderContract"/> plus a catalog reference in the selected
/// scope. Attribute constructors do not construct contracts at runtime. Declare the program's stages
/// with <see cref="ShaderStageAttribute"/> and its option uses with <see cref="ShaderUseAttribute"/>.
/// Repeat this attribute with different member names for a shader family; the generator also publishes
/// a read-only <c>Contracts</c> collection when the class owns multiple programs.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
internal sealed class ShaderProgramAttribute : Attribute
{
    #region Declaration
    /// <summary>Declares the generated contract member, catalog identity and structural variant limit.</summary>
    /// <param name="member">Generated static contract property name, such as <c>Contract</c> or <c>CopyContract</c>. Other attributes on this class refer to this member name.</param>
    /// <param name="identity">Stable program lookup identity within its scope, such as <c>lumon_combine</c>; this is not the C# member name.</param>
    /// <param name="variantBudget">Positive maximum number of supported structural assignments. Numeric specialization values do not increase this count.</param>
    public ShaderProgramAttribute(string member, string identity, int variantBudget) { }

    /// <summary>Gets or sets the generated catalog scope; defaults to <c>production</c>.</summary>
    /// <remarks>Use a separate scope for isolated fixtures so production builds do not require their assets. Scope selection is compile-time catalog metadata, not a runtime option.</remarks>
    public string Scope { get; set; } = "production";
    #endregion
}
