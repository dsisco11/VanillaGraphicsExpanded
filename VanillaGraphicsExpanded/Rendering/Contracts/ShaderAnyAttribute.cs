using System;

namespace VanillaGraphicsExpanded.Rendering.Contracts;

/// <summary>Defines a named logical OR of other conditions declared on the same class.</summary>
/// <remarks>
/// The generator resolves child names into a ShaderCondition.Any node; at least one child must evaluate
/// true. An empty child list evaluates false. Use the node name in ShaderUse.When or as a child of
/// another condition to compose specialization availability. Names must be unique, all children must
/// exist, and cycles are rejected. Every referenced option must be structural in a consuming stage.
/// This expression controls availability only; it does not filter supported program assignments.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
internal sealed class ShaderAnyAttribute : Attribute
{
    #region Declaration
    /// <summary>Names a disjunction of existing condition nodes.</summary>
    /// <param name="name">Unique condition identifier on this class, usable by ShaderUse.When and other conditions.</param>
    /// <param name="children">Condition identifiers on this class of which at least one must be true; an empty list is false.</param>
    public ShaderAnyAttribute(string name, params string[] children) { }
    #endregion
}
