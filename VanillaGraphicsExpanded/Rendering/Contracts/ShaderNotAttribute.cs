using System;

namespace VanillaGraphicsExpanded.Rendering.Contracts;

/// <summary>Defines a named logical negation of another condition declared on the same class.</summary>
/// <remarks>
/// The generator resolves the child into a ShaderCondition.Not node, which is true exactly when its
/// child is false. Use the node name in ShaderUse.When or in an All/Any/Not composition to control
/// specialization availability. Names must be unique, the child must exist, and cycles are rejected.
/// Every referenced option must be structural in a consuming stage. Negation affects the availability
/// of a specialization input, not whether the program configuration is supported.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
internal sealed class ShaderNotAttribute : Attribute
{
    #region Declaration
    /// <summary>Names the logical inverse of an existing condition node.</summary>
    /// <param name="name">Unique condition identifier on this class, usable by ShaderUse.When and other conditions.</param>
    /// <param name="child">Condition identifier on this class whose Boolean result is inverted.</param>
    public ShaderNotAttribute(string name, string child) { }
    #endregion
}
