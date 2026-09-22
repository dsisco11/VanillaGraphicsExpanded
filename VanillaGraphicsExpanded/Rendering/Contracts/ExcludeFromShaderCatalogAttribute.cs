using System;

namespace VanillaGraphicsExpanded.Rendering.Contracts;

/// <summary>Excludes a shader owner from the legacy reflection-based contract catalog.</summary>
/// <remarks>
/// ShaderContractDiscovery skips a class carrying this marker before reading its static contracts.
/// Use it for handwritten declarations that belong only to a separately constructed validation scope.
/// It does not disable contract creation or prevent direct use of those declarations, and it is not
/// inherited by derived classes. Generated owners are excluded from legacy discovery automatically;
/// control their catalog membership with <see cref="ShaderProgramAttribute.Scope"/> instead.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
internal sealed class ExcludeFromShaderCatalogAttribute : Attribute { }
