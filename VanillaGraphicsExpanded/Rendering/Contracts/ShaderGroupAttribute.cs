using System;

namespace VanillaGraphicsExpanded.Rendering.Contracts;

/// <summary>Publishes a named, immutable group of option keys for explicit reuse by shader programs.</summary>
/// <remarks>
/// Apply to the partial class that exposes the listed attributed option properties. The generator emits
/// a static <see cref="ShaderOptionGroup"/> member referring to their existing keys; it does not copy
/// defaults or create mutable settings. A program accepts the group through
/// <see cref="ShaderAcceptGroupAttribute"/>. Group membership describes accepted settings, including
/// membership used for global-setting projection; it does not assign structural or specialization uses
/// to stages. Declare those separately with <see cref="ShaderUseAttribute"/>.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
internal sealed class ShaderGroupAttribute : Attribute
{
    #region Declaration
    /// <summary>Names the generated shared group and selects its existing option keys.</summary>
    /// <param name="member">Generated static group property name. Use this literal name when another attribute references the group; it does not exist in source for nameof binding.</param>
    /// <param name="identity">Stable group identity used for program membership and settings projection.</param>
    /// <param name="options">Attributed option property names on this class, not GLSL names. Canonical option names must not repeat within the group.</param>
    public ShaderGroupAttribute(string member, string identity, params string[] options) { }
    #endregion
}
