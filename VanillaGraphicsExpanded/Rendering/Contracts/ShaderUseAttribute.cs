using System;

namespace VanillaGraphicsExpanded.Rendering.Contracts;

/// <summary>Declares how one program stage consumes an option exposed by the attributed class.</summary>
/// <remarks>
/// With the default SpecializationId of -1, the option is structural: its finite Boolean, integer or enum
/// domain contributes to compiled variants and binary selection. A nonnegative ID instead declares a
/// specialization constant whose selected value is supplied when loading the shader, without multiplying
/// binaries. Repeat for each consuming stage or program; one option may be structural in one stage and
/// specialized in another. Uses also make the option accepted by the program. These declarations describe
/// configuration inputs, not ordinary uniforms, buffer fields or resource bindings.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
internal sealed class ShaderUseAttribute : Attribute
{
    #region Declaration
    /// <summary>Associates a local option property with an explicitly declared program stage.</summary>
    /// <param name="program">Generated contract member named by ShaderProgram on this class.</param>
    /// <param name="stage">Stage kind already declared for that program by ShaderStage.</param>
    /// <param name="option">Attributed property name on this class, normally <c>nameof(Property)</c>; not its GLSL name or generated key name.</param>
    public ShaderUseAttribute(string program, ShaderStageKind stage, string option) { }

    /// <summary>Gets or sets the explicit stage-local specialization ID, or -1 for structural use.</summary>
    /// <remarks>The default is -1. Specialization IDs must be nonnegative and unique within the stage, and remain stable when declarations are reordered. Other negative values are invalid.</remarks>
    public int SpecializationId { get; set; } = -1;

    /// <summary>Gets or sets a restricted Boolean expression controlling specialization availability.</summary>
    /// <remarks>
    /// Null or omission is unconditional; empty text is invalid. Reference this class's attributed property
    /// names (including shared-key references), not GLSL names or aliases. Bare properties must be Boolean.
    /// Supported operators are !, ==, &amp;&amp; and || with C# precedence and parentheses. Equality requires a
    /// property on the left and an exactly typed literal on the right: true/false, int literals (including
    /// negative values), uint literals with u suffix, or EnumType.Member from the property's enum domain.
    /// Namespace-qualified and global-qualified enum types are accepted; using aliases and arbitrary constants
    /// are not. Integer literals support decimal, hexadecimal, binary and digit separators; no numeric coercion,
    /// casts, arithmetic, calls, option-to-option comparisons or != operator are supported.
    /// Only specialization uses may have conditions; every dependency must be structural in this stage.
    /// The generator validates and lowers the expression into ShaderCondition, with no runtime parsing.
    /// Inactive inputs retain their selected values. Names inside strings do not participate in C# rename;
    /// stale names cause a build diagnostic on this attribute.
    /// </remarks>
    public string? When { get; set; }
    #endregion
}
