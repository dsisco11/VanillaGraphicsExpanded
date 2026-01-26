namespace VanillaGraphicsExpanded.PBR.Materials.Cache;

/// <summary>
/// Average linear RGB encoded as IEEE754 half bit patterns (RGB16F).
/// </summary>
internal readonly record struct BaseColorRgb16f(ushort R, ushort G, ushort B);
