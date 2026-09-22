using System;

namespace VanillaGraphicsExpanded.Rendering.Contracts;

/// <summary>Keeps isolated validation-only shader owners outside automatic packaged-program discovery.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
internal sealed class ExcludeFromShaderCatalogAttribute : Attribute { }
