using System;

namespace VanillaGraphicsExpanded.Rendering.Contracts;

/// <summary>Declares one complete supported combination of a program's structural option values.</summary>
/// <remarks>
/// Without these attributes, the generator uses the Cartesian product of the structural option domains.
/// Adding one or more replaces that product with exactly the listed rows. Each row must specify every
/// structural option once using its canonical GLSL name; aliases and specialization-only settings are
/// not allowed. Rows must be distinct, include the default combination and fit the program's variant
/// budget. Values use the shared typed parser, so invalid domain values and fractional integers fail
/// validation. For example, a row can contain <c>FEATURE=1</c> and <c>MODE=2</c>.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
internal sealed class ShaderAssignmentAttribute : Attribute
{
    #region Declaration
    /// <summary>Adds one explicit supported structural assignment to a program's finite row list.</summary>
    /// <param name="program">Generated contract member named by ShaderProgram on this class.</param>
    /// <param name="values">Complete canonical <c>GLSL_NAME=value</c> pairs, one per structural option; Boolean values may use <c>0</c> or <c>1</c>.</param>
    public ShaderAssignmentAttribute(string program, params string[] values) { }
    #endregion
}
