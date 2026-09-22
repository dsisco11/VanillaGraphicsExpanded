using System;

namespace VanillaGraphicsExpanded.Rendering.Contracts;

/// <summary>Adds an explicit source stage and binding layout to a shader program's generated contract.</summary>
/// <remarks>
/// Apply once for each stage kind used by the named program on the same partial class.
/// Graphics programs require vertex and fragment stages; a compute program contains only a compute
/// stage. Tessellation control requires tessellation evaluation. Compatible declarations sharing an
/// identity within a scope reuse one immutable <see cref="ShaderStageContract"/>. Source, entry point,
/// layout, fixed defines and option uses must agree for that identity; use a different identity when
/// the same source needs a different configuration. Every generated stage includes the fixed
/// <c>VGE_SPIRV_BUILD=1</c> build profile.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
internal sealed class ShaderStageAttribute : Attribute
{
    #region Declaration
    /// <summary>Declares a stage's program membership, pipeline kind and source asset.</summary>
    /// <param name="program">Generated contract member named by ShaderProgram on this class, not its catalog identity.</param>
    /// <param name="kind">Pipeline stage kind; each program may declare a kind only once.</param>
    /// <param name="source">Shader source path relative to the shader asset root, including its extension, such as <c>shared/fullscreen.vsh</c>.</param>
    public ShaderStageAttribute(string program, ShaderStageKind kind, string source) { }

    /// <summary>Gets or sets the shared stage identity; when omitted, the source asset path is used.</summary>
    /// <remarks>Equal identities within a scope require compatible complete stage definitions. Identity also supplies the default BinaryAsset value.</remarks>
    public string? Identity { get; set; }

    /// <summary>Gets or sets the binding-layout key passed to GpuShaderContracts.DeclareBindings.</summary>
    /// <remarks>When omitted, the source path with its final extension removed selects the existing resource/interface binding contract. This key does not declare option uses.</remarks>
    public string? Layout { get; set; }

    /// <summary>Gets or sets the shader entry-point identifier; defaults to <c>main</c>.</summary>
    public string EntryPoint { get; set; } = "main";

    /// <summary>Gets or sets the relative binary asset base path; when omitted, the stage identity is used.</summary>
    /// <remarks>The default configuration appends <c>.spv</c>; other configurations use <c>variants/&lt;BinaryAsset&gt;/&lt;key hash&gt;.spv</c>. Do not append the generated .spv suffix here.</remarks>
    public string? BinaryAsset { get; set; }
    #endregion
}
