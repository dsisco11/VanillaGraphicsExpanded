using System;

namespace VanillaGraphicsExpanded.Rendering.Contracts;

/// <summary>Includes a shared option group in one program's accepted settings.</summary>
/// <remarks>
/// Apply on the class declaring the program. The generator resolves the group declared by
/// <see cref="ShaderGroupAttribute"/> on the referenced owner and includes that same group in the
/// generated program contract. This permits settings validation and group-based projection without
/// repeating option definitions. It does not make every group member a stage input: use local
/// option references and <see cref="ShaderUseAttribute"/> to declare actual stage dependencies.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
internal sealed class ShaderAcceptGroupAttribute : Attribute
{
    #region Declaration
    /// <summary>Connects one program declaration to a generated group on another owner or this class.</summary>
    /// <param name="program">Generated contract member named by ShaderProgram on this class.</param>
    /// <param name="owner">Partial class declaring the group through ShaderGroup.</param>
    /// <param name="member">Generated group property name from ShaderGroup's member argument, not the group's identity.</param>
    public ShaderAcceptGroupAttribute(string program, Type owner, string member) { }
    #endregion
}
