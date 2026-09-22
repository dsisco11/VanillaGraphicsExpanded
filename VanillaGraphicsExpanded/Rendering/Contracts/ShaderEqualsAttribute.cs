using System;

namespace VanillaGraphicsExpanded.Rendering.Contracts;

/// <summary>Defines a named condition that compares a structural option with a typed constant.</summary>
/// <remarks>
/// Conditions belong to the attributed class. Reference this node from ShaderUse.When to control
/// specialization availability, or combine it with <see cref="ShaderAllAttribute"/>,
/// <see cref="ShaderAnyAttribute"/> and <see cref="ShaderNotAttribute"/>. The generator creates a
/// <see cref="ShaderCondition"/> tree evaluated by the shared build/runtime selection model; it does
/// not execute a callback. The option must be structural in every stage using the condition. A false
/// condition omits that specialization input without discarding its selected value or rejecting the
/// structural configuration. Condition names must be unique within the class.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
internal sealed class ShaderEqualsAttribute : Attribute
{
    #region Declaration
    /// <summary>Names a typed equality node for use in a specialization's availability expression.</summary>
    /// <param name="name">Condition identifier referenced by ShaderUse.When or another condition on this class.</param>
    /// <param name="option">Attributed option property name on this class, not its canonical GLSL name.</param>
    /// <param name="value">Constant exactly matching the option's Boolean, integer or enum type and satisfying its domain and bounds.</param>
    public ShaderEqualsAttribute(string name, string option, object value) { }
    #endregion
}
