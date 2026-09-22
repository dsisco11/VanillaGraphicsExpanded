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

    /// <summary>Gets or sets the named condition controlling specialization availability; null means always available.</summary>
    /// <remarks>Refer to an Equals/All/Any/Not declaration on this class. Only specialization uses may have a condition, and its dependencies must be structural options of this stage. An inactive input retains its selected setting value.</remarks>
    public string? When { get; set; }
    #endregion
}
