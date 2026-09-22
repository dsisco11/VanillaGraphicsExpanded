using System;

namespace VanillaGraphicsExpanded.Rendering.Contracts;

/// <summary>Adds a typed preprocessor define whose value is fixed for every configuration of one stage.</summary>
/// <remarks>
/// The generator adds the value to the stage contract's fixed defines for source emission during offline
/// compilation. It is not a mutable option, specialization constant or extra variant dimension.
/// Changing it requires rebuilding the shader assets. Names must not duplicate another fixed define or
/// conflict with configurable setting names or aliases. VGE_SPIRV_BUILD is already supplied by the
/// generated build profile and must not be redeclared here. Incompatible fixed configurations of a
/// shared source require distinct stage identities.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
internal sealed class ShaderFixedDefineAttribute : Attribute
{
    #region Declaration
    /// <summary>Declares a constant source macro for a particular program stage.</summary>
    /// <param name="program">Generated contract member named by ShaderProgram on this class.</param>
    /// <param name="stage">Stage kind already declared for the program.</param>
    /// <param name="name">Canonical GLSL preprocessor identifier to emit.</param>
    /// <param name="value">Compile-time bool, int, uint, finite float or 32-bit enum constant, encoded through ShaderScalar.</param>
    public ShaderFixedDefineAttribute(string program, ShaderStageKind stage, string name, object value) { }
    #endregion
}
