namespace VanillaGraphicsExpanded.PBR;

/// <summary>Defines shared shading-quality levels and their currently supported resource budgets.</summary>
internal static class ShadingQuality
{
    #region Public API
    /// <summary>Preserves the highest qualified shading resolution when no preference is saved.</summary>
    internal const int Default = 3;

    /// <summary>Normalizes unsupported persisted or live values to the default quality level.</summary>
    internal static int Normalize(int value) => value is >= 0 and <= 3 ? value : Default;

    /// <summary>Maps the shared quality level to the material-energy texture's square dimension.</summary>
    internal static int EnergyLookupSize(int quality) => 16 << Normalize(quality);
    #endregion
}
