using System;

namespace VanillaGraphicsExpanded.Rendering.Contracts;

/// <summary>Defines a named logical AND of other conditions declared on the same class.</summary>
/// <remarks>
/// The generator resolves child names into a ShaderCondition.All node; all children must evaluate true.
/// An empty child list evaluates true. Use the node name in ShaderUse.When or as a child of another
/// condition to compose specialization availability. Names must be unique, all children must exist,
/// and cycles are rejected. Every referenced option must be structural in a consuming stage.
/// This expression controls availability only; it does not filter supported program assignments.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
internal sealed class ShaderAllAttribute : Attribute
{
    #region Declaration
    /// <summary>Names a conjunction of existing condition nodes.</summary>
    /// <param name="name">Unique condition identifier on this class, usable by ShaderUse.When and other conditions.</param>
    /// <param name="children">Condition identifiers on this class whose results must all be true; an empty list is true.</param>
    public ShaderAllAttribute(string name, params string[] children) { }
    #endregion
}
